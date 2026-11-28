import argparse
import csv
import io
import json
import re
import struct
from collections import Counter, defaultdict
from contextlib import ExitStack
from functools import lru_cache
from pathlib import Path
from time import perf_counter

import UnityPy
from UnityPy.files import SerializedFile
from UnityPy.helpers.CompressionHelper import decompress_lz4
from UnityPy.streams import EndianBinaryReader

# ActionDataAsset inheritance verified against the public build's IL2CPP metadata.
ACTIONS = {name + 'Asset' for name in '''
ActionData AimedTentacleSlamAttackData AnimatedActionData AnimatedActionDataBase AnimatedActionDataCommon
AnimatedCinematicStrategyData BalancingMovementData BonfireRestActionData BowAttackData BowMultishotAttackData
BurnEffigyActionData ChargedMagicActionData ChargerAttackData CinematicActionData CinematicActionDataBase
CinematicStrategyData CommunityChestActionData ConsumeItemActionData ContributeWhisperUpgradeItemActionData
CrossBossGateActionData CrucibleEndOfFloorCinematicActionData CrucibleTeleportActionData DeathReactionData
DodgeActionData DodgeRollCinematicStrategyData DodgeRollData EdgeClimbingData EmptyCinematicActionData
EnterCrucibleActionData FallData FallImpactReactionData FarmToolActionData FastTravelActionData
GrabAndThrowBarrelData GrenadeThrowActionData GroundMovementData HealMinionsActionData HeroKnockdownData
HitReactionData HitReactionDataBase HouseCraftingTableActionData IchorInjectionCinematicActionData
InputBlockingActionData InteractionData InteractionDataBase InvisibilityActionData JumpData
KillCreepHeartActionData KillEntityCinematicStrategyData KnockdownGetUpData LadderClimbingData LeapAttackData
LockEntityCinematicStrategyData LootContainerActionData MakeCrucibleOfferingActionData MeleeAttackData
MeleeAttackDataBase MeleeAttackWindupData MoveToCinematicStrategyData NpcInteractionData NpcTradeItemWithPlayerData
OpenDoorActionData PairedActionDummyActionData ParryActionData PhaseTransitioningProjectileAttackData
PlaceHouseItemActionData PlantSeedActionData PlayerToPlayerInteractionData PlayerTurnActionData PulledMovementData
PurchaseActionData PushCogActionData PushPullActionData QuickStepData RaiseMinionActionData RangedAttackData
ReadableInteractionData RefillWateringCanActionData ResourceRefineryInteractData ResurrectActionData
ReturnSpellActionData SimpleInputBlockingActionData SirenSpawnActionData SitActionData SitOnChairActionData
SleepInHouseBedActionData SneakingData SpiderBatBossScreamAttackData SplitActionData StartLoopEndActionData
StuntActionData TeleportActionData TeleportToLocationActionData TeleportToLocationActionDataBase TurnWheelActionData
UnsheatheData VisualOnlyActionData WallClimbingData WallClimbingJumpData WallGrabData WallMovementActionData
WaterMovementData WhisperInteractionActionData
'''.split()}
NPCS = {'NpcDataAsset', 'NpcGenericDataAsset', 'NpcHumanoidEnemyDataAsset'}
ENEMY_TYPES = dict(enumerate(('Normal', 'Giant', 'Elite', 'Boss', 'Critters', 'PlotEnemy')))
ASSET_REF = rb'\x81\xa2Id\x81\xa5Value'
RECORD = re.compile(rb'\x92[\xce\xd2](.{4})(?:[\x81-\x8f]|\xde..|\xdf....)'
                    rb'(?:\xb7InteractionDataAssetRef' + ASSET_REF + rb'(?:\x00|\xcf.{8}))?'
                    rb'(?:\xb2EntityDataAssetRef' + ASSET_REF + rb'(?:\x00|\xcf.{8})'
                    rb'\xb6ControllerDataAssetRef' + ASSET_REF + rb'(?:\x00|\xcf.{8}))?'
                    rb'\xa4Guid\x81\xa5Value\xcf(.{8})', re.S)
REF = re.compile(ASSET_REF + rb'\xcf(.{8})', re.S)


class NodeStream(io.RawIOBase):
    def __init__(self, file, offset, size):
        self.file, self.offset, self.size, self.pos = file, offset, size, 0

    def readable(self):
        return True

    def seekable(self):
        return True

    def tell(self):
        return self.pos

    def seek(self, offset, whence=0):
        self.pos = (0, self.pos, self.size)[whence] + offset
        return self.pos

    def readinto(self, dest):
        self.file.seek(self.offset + self.pos)
        n = self.file.readinto(memoryview(dest)[:max(0, self.size - self.pos)])
        self.pos += n
        return n


def qdb_files(path, stack):
    # This build stores the CAB and the large resource stream in an uncompressed UnityFS block.
    file = stack.enter_context(path.open('rb', buffering=0))
    r = EndianBinaryReader(file)
    if r.read_string_to_null() != 'UnityFS' or r.read_u_int() < 7:
        raise ValueError('Unsupported qdb bundle header')
    r.read_string_to_null()
    r.read_string_to_null()
    size = r.read_u_long()
    packed, raw, flags = r.read_u_int_array(3)
    r.align_stream(16)
    start = r.Position
    if flags & 0x80:
        r.Position = size - packed
    info = r.read_bytes(packed)
    if not flags & 0x80:
        start = r.Position
    if flags & 0x200:
        start = (start + 15) & ~15
    if flags & 63 in (2, 3):
        info = decompress_lz4(info, raw)
    elif flags & 63:
        raise ValueError('Unsupported UnityFS directory codec')
    r = EndianBinaryReader(info)
    r.Position = 16
    for _ in range(r.read_int()):
        raw, packed = r.read_u_int_array(2)
        if r.read_u_short() & 63 or raw != packed:
            raise ValueError('Expected uncompressed qdb data blocks')
    for _ in range(r.read_int()):
        offset, length, kind = r.read_long(), r.read_long(), r.read_u_int()
        name = r.read_string_to_null()
        if kind & 4:
            stream = stack.enter_context(io.BufferedReader(NodeStream(file, start + offset, length), 32768))
            yield SerializedFile(EndianBinaryReader(stream), name=name)


def read_assets(bundle_dir):
    scripts = {}
    for path in sorted(bundle_dir.glob('*_monoscripts_*.bundle')):
        for obj in UnityPy.load(str(path)).objects:
            if obj.type.name == 'MonoScript':
                scripts[(obj.assets_file.name.lower(), obj.path_id)] = obj.read().m_ClassName
    assets, rows, skips, pointers = {}, [], [], {}
    (path,) = sorted(bundle_dir.glob('qdb_assets_all_*.bundle'))
    with ExitStack() as stack:
        for sf in qdb_files(path, stack):
            for path, ptr in sorted(sf.container.items()):
                obj = ptr.deref()
                if obj.type.name != 'MonoBehaviour':
                    continue
                try:
                    tree = obj.read_typetree()
                except Exception as exc:
                    raise ValueError(f'{path}：类型树读取失败') from exc
                value = tree.get('Identifier', {}).get('Guid') or tree.get('AssetGuid')
                guid = value.get('Value') if isinstance(value, dict) else value
                if not guid:
                    skips.append((path, {k: tree[k] for k in ('m_Name', 'm_Script', 'AssetGuid', 'Identifier') if k in tree}))
                    continue
                ref = tree['m_Script']
                script_file = (obj.assets_file.externals[ref['m_FileID'] - 1].name.lower()
                               if ref['m_FileID'] else obj.assets_file.name.lower())
                key = script_file, ref['m_PathID']
                if key not in scripts:
                    local = obj.assets_file.objects.get(key[1]) if not ref['m_FileID'] else None
                    if local is None or local.type.name != 'MonoScript':
                        raise ValueError(f'{path}：无法解析 MonoScript：{ref}')
                    scripts[key] = local.read().m_ClassName
                row = {'guid': str(guid), 'class': scripts[key], 'path': path, 'name': tree['m_Name']}
                if guid in assets and pointers[guid] != (obj.assets_file.name, obj.path_id):
                    raise ValueError(f'不同资产使用同一 GUID {guid}：{assets[guid]["path"]} / {path}')
                if guid not in assets:
                    assets[guid] = row
                    pointers[guid] = obj.assets_file.name, obj.path_id
                    rows.append(row)
    return assets, rows, skips


NUMBERS = dict(zip(range(0xca, 0xd4), map(struct.Struct, ('>f', '>d', '>B', '>H', '>I', '>Q', '>b', '>h', '>i', '>q'))))


def unpack(data, pos):
    # Decode named field values, not the whole record: Moon also writes positional values in maps.
    def read():
        nonlocal pos
        tag = data[pos]
        pos += 1
        if tag <= 0x7f or tag >= 0xe0:
            return tag if tag <= 0x7f else tag - 256
        if 0x80 <= tag <= 0xbf:
            count = tag & (31 if tag >= 0xa0 else 15)
            kind = ('map' if tag < 0x90 else 'array' if tag < 0xa0 else 'str')
        elif tag in NUMBERS:
            fmt = NUMBERS[tag]
            val = fmt.unpack_from(data, pos)[0]
            pos += fmt.size
            return val
        elif tag in (0xc0, 0xc2, 0xc3):
            return {0xc0: None, 0xc2: False, 0xc3: True}[tag]
        elif 0xd4 <= tag <= 0xd8:
            pos += 1 + (1 << (tag - 0xd4))
            return None
        else:
            kind, width = {
                0xc4: ('bin', 1), 0xc5: ('bin', 2), 0xc6: ('bin', 4),
                0xc7: ('ext', 1), 0xc8: ('ext', 2), 0xc9: ('ext', 4),
                0xd9: ('str', 1), 0xda: ('str', 2), 0xdb: ('str', 4),
                0xdc: ('array', 2), 0xdd: ('array', 4), 0xde: ('map', 2), 0xdf: ('map', 4),
            }[tag]
            count = int.from_bytes(data[pos:pos + width], 'big')
            pos += width
        if kind == 'map':
            out = {}
            for _ in range(count):
                key = read()
                value = read()
                out[key if isinstance(key, (str, int)) else repr(key)] = value
            return out
        if kind == 'array':
            return [read() for _ in range(count)]
        if kind == 'ext':
            extension = data[pos]
            pos += 1
            if extension in (11, 12, 13, 37):
                # Moon's raw arrays add a MessagePack length and boolean before the binary payload.
                length, flag = read(), read()
                if length != count or type(flag) is not bool:
                    raise ValueError(f'Unexpected raw-array header for extension {extension}')
        start = pos
        pos += count
        if kind == 'str':
            return data[start:pos].decode('utf-8')
        return None

    result = read()
    return result, pos


def runtime_index(path):
    with path.open('rb') as f:
        magic, size, count = struct.unpack('<8sII', f.read(16))
        if magic != b'MOON_QDB':
            raise ValueError('Expected MOON_QDB runtime database')
        f.seek(count * 8, 1)
        packed = f.read(size)
    tag, length, ext, number, raw = struct.unpack_from('>BIBBI', packed)
    if (tag, ext, number) != (0xc9, 99, 0xd2) or length != size - 6:
        raise ValueError('Unexpected MOON_QDB runtime compression wrapper')
    data = decompress_lz4(memoryview(packed)[11:], raw)
    matches = list(RECORD.finditer(data))
    if data[0] != 0xdc or len(matches) != struct.unpack_from('>H', data, 1)[0] or matches[0].start() != 3:
        raise ValueError('Runtime record boundary count disagrees with the root array')
    index = {}
    for i, m in enumerate(matches):
        guid = int.from_bytes(m[2], 'big')
        end = matches[i + 1].start() if i + 1 < len(matches) else len(data)
        index[guid] = m.start(), end, m[1]
    if len(index) != len(matches):
        raise ValueError('Duplicate runtime GUID')
    return data, index


def parallel_map(data, pos, depth):
    keys, pos = unpack(data, pos)
    if not isinstance(keys, list):
        raise ValueError('expected parallel key array')
    if depth == 1:
        values, pos = unpack(data, pos)
    else:
        tag = data[pos]
        width = {0xdc: 2, 0xdd: 4}.get(tag, 0)
        count = int.from_bytes(data[pos+1:pos+1+width], 'big') if width else tag - 0x90
        pos += 1 + width
        values = []
        for _ in range(count):
            value, pos = parallel_map(data, pos, depth - 1)
            values.append(value)
    if not isinstance(values, list) or len(keys) != len(values):
        raise ValueError('parallel arrays disagree')
    return dict(zip(keys, values)), pos


def guid_of(value):
    return value.get('Id', {}).get('Value', 0) if isinstance(value, dict) and isinstance(value.get('Id'), dict) else 0


def visit_refs(value, path):
    if isinstance(value, dict):
        if 'Id' in value and isinstance(value['Id'], dict) and 'Value' in value['Id']:
            if g := guid_of(value):
                yield path, g
            return
        for key, child in value.items():
            yield from visit_refs(child, f'{path}.{key}' if isinstance(key, str) else f'{path}[{key}]')
    elif isinstance(value, list):
        for i, child in enumerate(value):
            yield from visit_refs(child, f'{path}[{i}]')


AI_FIELDS = '''IdleConfig CombatConfig AlertConfig ChaseConfig AggroConfig UnsheatheConfig PatrolConfig
BehaviorConfig UnblockPathConfig RangedRetaliationConfig IdleActions IdleAction IdleSafeAction SplinePathAction
AttentionSeekingAction FailedFollowingAction SuccessFollowingAction SuccessFollowingLoop BurrowAction
UnburrowAction AlertedAction PreEncounterAnim EncounterAnimTransition'''.split()
ENTITY_FIELDS = '''GroundMovementData GroundMovementDataPhase2 WaterMovementData PulledMovementData FallData
BalancingMovementData KnockdownData WallClimbingData HitReactionData RecoilReaction ParriedHitReaction
DamageResolverData OptionalBackstabPairedActions OptionalRipostePairedActions InteractAction SpawnAction'''.split()
REACTIONS = '''DefaultHitReaction StaggerHitReaction DuelStaggerHitReaction DeathHitReaction
NoWeaponRecoilReaction OneHandedRecoilReactions TwoHandedRecoilReactions'''.split()
FOLLOWUPS = '''AutoFollowupAction RecoilReaction ParriedHitReaction NextActionDataLink ResourceFinisherDataLink
PairedAnim PostStuntAction'''.split()
BRANCHES = {
    'EnemyWeaponDataAsset': ('武器／副手', ['RecoilReaction', 'ParriedHitReaction']),
    'AffixMovesetDataAsset': ('特质动作集', []),
    'DamageResolverDataAsset': ('受击／死亡反应', REACTIONS),
    'FinisherWeaponConfigAsset': ('处决配置', ['FinisherPairs']),
    'PairedActionDataAsset': ('双人动作链接', ['ConvertedAction']),
    'FinisherPairedActionDataAsset': ('双人动作链接', ['ConvertedAction']),
    'WolfPairedActionDataAsset': ('双人动作链接', ['ConvertedAction']),
}


def build_tables(assets, data, index):
    problems = set()
    types = defaultdict(set)
    for guid, row in assets.items():
        if guid in index:
            types[index[guid][2]].add(row['class'])

    @lru_cache(None)
    def asset(guid):
        if guid in assets:
            return assets[guid]
        classes = types[index[guid][2]] if guid in index else ()
        return {'guid': str(guid), 'class': next(iter(classes)) if len(classes) == 1 else None,
                'name': None, 'path': None}

    @lru_cache(None)
    def field(guid, key, *, last=False, map_depth=0):
        start, end, _ = index[guid]
        encoded = key.encode('ascii')
        token = (bytes([0xa0 + len(encoded)]) if len(encoded) < 32 else bytes([0xd9, len(encoded)])) + encoded
        pos = (data.rfind if last else data.find)(token, start, end)
        if pos == -1:
            return None
        try:
            offset = pos + len(token)
            value, after = parallel_map(data, offset, map_depth) if map_depth else unpack(data, offset)
            if after > end:
                raise ValueError('field crosses record boundary')
            return value
        except (IndexError, KeyError, TypeError, ValueError, struct.error) as exc:
            problems.add(f'{asset(guid)["class"]}.{key} ({guid})：{exc}；片段 {data[pos:min(pos+54, end)]!r}')
            return None

    @lru_cache(None)
    def edges(guid, role):
        cls = asset(guid)['class']
        out = []
        if guid not in index:
            problems.add(f'{cls} {guid}：在主运行库找不到记录')
            return out
        for key in (AI_FIELDS if role == 'AI' else ENTITY_FIELDS if role == '本体' else
                    FOLLOWUPS if role == '动作衔接' else BRANCHES.get(cls, (None, []))[1]):
            for path, target in visit_refs(field(guid, key), key):
                out.append((path, target, {}))
        if cls == 'DamageResolverDataAsset':
            for key in ('KnockdownHitReactionMap', 'SectionToHitReaction', 'PoiseReactionMap'):
                depth = 2 if key == 'PoiseReactionMap' else 1
                out.extend((path, target, {}) for path, target in visit_refs(field(guid, key, map_depth=depth), key))
        if role == 'AI':
            for key in ('Weapon', 'OffHandItem', 'AffixMoveset'):
                if g := guid_of(field(guid, key)):
                    out.append((key, g, {}))
        if role == 'AI' or cls in ('EnemyWeaponDataAsset', 'AffixMovesetDataAsset'):
            spider = cls == 'SpiderBatBossAiControllerDataAsset'
            action_list = field(guid, 'ActionList', last=spider) or {}
            unlocks = field(guid, 'UnlockActions') or {}
            out.extend(groups(action_list, 'ActionList'))
            if spider:
                overwhelmed = field(guid, 'OverwhelmedConfig') or {}
                out.extend(groups(overwhelmed.get('ActionList') or {}, 'OverwhelmedConfig.ActionList'))
            for i, item in enumerate(unlocks.get('Actions') or []):
                out.extend(selection(item.get('Action'), f'UnlockActions.Actions[{i}].Action',
                                     {'min_level': item.get('MinLevel')}))
            if cls == 'AffixMovesetDataAsset':
                for i, item in enumerate(action_list.get('Actions') or []):
                    out.extend(selection(item.get('Action'), f'ActionList.Actions[{i}].Action',
                                         {'required_affix_guid': str(guid_of(item.get('Affix')))}))
        if role in ('AI', '本体', '武器／副手', '受击／死亡反应', '特质动作集'):
            start, end, _ = index[guid]
            known = {g for _, g, _ in out}
            for m in REF.finditer(data, start, end):
                target = int.from_bytes(m[1], 'big')
                if target not in known and asset(target)['class'] in ACTIONS:
                    problems.add(f'{cls} ({guid})：未归属的 ActionData 引用 {target}，片段 '
                                 f'{data[max(start, m.start()-36):m.end()]!r}')
        return out

    def groups(action_list, path):
        list_muted = (action_list.get('DebugParams') or {}).get('Mute')
        for i, group in enumerate(action_list.get('ActionGroups') or []):
            debug = group.get('DebugParams') or {}
            for j, choice in enumerate(group.get('Actions') or []):
                choice_debug = choice.get('DebugParams') or {}
                meta = {'group': debug.get('Name') or f'#{i}', 'selection': choice_debug.get('Name'),
                        'muted': bool(list_muted or debug.get('Mute') or choice_debug.get('Mute')),
                        'solo': bool(debug.get('Solo') or choice_debug.get('Solo'))}
                yield from selection(choice, f'{path}.ActionGroups[{i}].Actions[{j}]', meta)

    def selection(choice, path, meta):
        if choice:
            if g := guid_of(choice.get('Data')):
                yield f'{path}.Data', g, meta
            for i, combo in enumerate(choice.get('ComboParams') or []):
                if g := guid_of(combo.get('Action')):
                    yield f'{path}.ComboParams[{i}].Action', g, meta

    npc_guids = {g for g, row in assets.items() if row['class'] in NPCS}
    npc_guids.update(g for g in index if asset(g)['class'] in NPCS)
    enemies, coverage = [], Counter()
    chains = set()
    for guid in sorted(npc_guids, key=lambda g: ((asset(g)['name'] or '').lower(), g)):
        npc = asset(guid)
        cls = npc['class'].removesuffix('Asset')
        sources = defaultdict(list)
        if guid not in index:
            raise ValueError(f'{npc["path"]} {guid}：无运行时 NPC 记录')
        enemy_type = field(guid, 'EnemyType')
        if enemy_type not in ENEMY_TYPES:
            raise ValueError(f'{npc["path"]} {guid}：未知 EnemyType：{enemy_type}')
        controller = guid_of(field(guid, 'ControllerData'))

        def traverse(current, role, chain, seen, meta):
            if current in seen:
                return
            owner = (asset(current)['class'] or 'Unknown').removesuffix('Asset')
            items = edges(current, role)
            for path, target, detail in items:
                record(target, role, (*chain, f'{owner}.{path}'), (*seen, current), {**meta, **detail})

        def record(target, role, chain, seen, meta):
            target_class = asset(target)['class']
            if target_class in ACTIONS:
                source = {'kind': role, 'chain': list(chain), **meta}
                if source not in sources[target]:
                    sources[target].append(source)
                    coverage[role] += 1
                    chains.add(' → '.join(re.sub(r'\[\d+\]', '[]', step) for step in chain))
                traverse(target, '动作衔接', chain, seen, meta)
            elif target_class in BRANCHES:
                traverse(target, BRANCHES[target_class][0], chain, seen, meta)
                if target_class.endswith('PairedActionDataAsset'):
                    non_actions.add(target)
            elif target_class is None and '.Conditions.' not in chain[-1]:
                problems.add(f'未定位引用 {target}：{" → ".join(chain[-2:])}')

        non_actions = set()
        if controller:
            traverse(controller, 'AI', (f'{cls}.ControllerData',), (), {})
        if entity := guid_of(field(guid, 'EntityData')):
            traverse(entity, '本体', (f'{cls}.EntityData',), (), {})
        if interaction := guid_of(field(guid, 'InteractionData')):
            record(interaction, '交互', (f'{cls}.InteractionData',), (), {})
        enemies.append({**npc, 'EnemyType': ENEMY_TYPES[enemy_type], 'enemy_type_value': enemy_type,
                        'controller': asset(controller) if controller else None,
                        'actions': [{**asset(g), 'sources': sources[g]}
                                    for g in sorted(sources, key=lambda g: (asset(g)['name'] or '', g))],
                        'related_paired_actions_not_action_data': [asset(g) for g in sorted(non_actions)]})

    gestures = {g for g, row in assets.items() if row['class'] == 'CollectableGestureDataAsset'}
    for g in sorted(gestures):
        target = guid_of(field(g, 'Gesture'))
        if target not in assets or assets[target]['class'] not in ACTIONS:
            problems.add(f'CollectableGestureData.Gesture ({g})：未定位表情动作 {target}')
        else:
            gestures.add(target)
    return enemies, [asset(g) for g in sorted(gestures)], chains, coverage, problems


def verify(assets, enemies, gestures):
    cases = (
        ('npcWarrickBoss', 2680182020363611965, 'NpcGenericDataAsset'),
        ('npcWarrickEnemy', 670058593051072033, 'NpcGenericDataAsset'),
        ('mutantSoldierAttackDelayedAOEData', 4386907888524794362, 'MeleeAttackDataAsset'),
        ('mutantSoldierAttackOverheadShortAOEData', 1578638975510328898, 'MeleeAttackDataAsset'),
        ('mutantSoldierAttackLeapFastData', 2630019745959013014, 'LeapAttackDataAsset'),
        ('mutantSoldierSwordAOEInnerData', 3660829114871757151, 'ContinuousAreaDamageDataAsset'),
        ('bestialPowerGestureData', 2126111680215882670, 'CollectableGestureDataAsset'),
        ('fisticuffsGestureData', 1770795828947168763, 'CollectableGestureDataAsset'),
    )
    by_guid = {row['guid']: row for row in enemies}
    check = []
    for name, guid, cls in cases:
        row = assets.get(guid, {})
        ok = (row.get('name'), row.get('class')) == (name, cls)
        if cls in NPCS:
            enemy = by_guid.get(str(guid), {})
            actions = {a['guid'] for a in enemy.get('actions', [])}
            ok &= {'4386907888524794362', '2630019745959013014'} <= actions and '3660829114871757151' not in actions
            ok &= enemy.get('EnemyType') == ('Boss' if name.endswith('Boss') else 'Giant')
        if cls == 'CollectableGestureDataAsset':
            ok &= any(g['guid'] == str(guid) for g in gestures)
        if cls == 'ContinuousAreaDamageDataAsset':
            ok &= cls not in ACTIONS
        line = f'{"PASS" if ok else "FAIL"} {name} = {guid} ({cls})'
        print(line, flush=True)
        check.append((ok, line))
    for guid in ('2680182020363611965', '670058593051072033'):
        npc = by_guid.get(guid)
        if npc and not any(a['guid'] == '1578638975510328898' for a in npc['actions']):
            print(f'NOTE {npc["name"]}: no verified reference to mutantSoldierAttackOverheadShortAOEData; asset GUID verified')
    return check


def write_tsv(path, columns, keys, rows):
    with path.open('w', encoding='utf-8', newline='') as f:
        writer = csv.writer(f, delimiter='\t', lineterminator='\n')
        writer.writerow(columns)
        writer.writerows([row[key] for key in keys] for row in rows)


def label(row):
    return f'{row["name"] or "（无 Unity 名称）"} `{row["guid"]}`'


def main():
    parser = argparse.ArgumentParser(description='Export offline Quantum asset, NPC action and gesture tables.')
    parser.add_argument('--bundle-dir', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    bundle_dir = args.bundle_dir.resolve()
    if args.out.resolve().is_relative_to(bundle_dir.parents[3]):
        parser.error('--out must be outside the game directory')
    started = perf_counter()
    assets, rows, skips = read_assets(bundle_dir)
    print(f'Unity assets: {len(rows)}', flush=True)
    data, index = runtime_index(bundle_dir.parents[1] / 'quantumDatabase.bin')
    print(f'Primary runtime records: {len(index)}', flush=True)
    enemies, gestures, chains, coverage, problems = build_tables(assets, data, index)
    checks = verify(assets, enemies, gestures)
    if not all(ok for ok, _ in checks):
        raise SystemExit('GUID checks failed')
    args.out.mkdir(parents=True, exist_ok=True)
    write_tsv(args.out / 'assets.tsv', ('guid', 'class', 'path', 'm_Name'), ('guid', 'class', 'path', 'name'), rows)
    write_tsv(args.out / 'gestures.tsv', ('guid', 'name', 'path'), ('guid', 'name', 'path'), gestures)
    with (args.out / 'enemies.json').open('w', encoding='utf-8') as f:
        json.dump(enemies, f, indent=2, ensure_ascii=False)
        f.write('\n')
    counts = Counter(row['class'] for row in rows)
    action_count = sum(counts[c] for c in ACTIONS)
    used = {a['guid'] for row in enemies for a in row['actions']}
    with (args.out / 'enemies.md').open('w', encoding='utf-8') as f:
        def line(text=''):
            print(text, file=f)

        line('# Quantum NPC 动作表')
        line(f'\nUnity GUID：{len(rows)}；NPC：{len(enemies)}；有动作：{sum(bool(n["actions"]) for n in enemies)}；'
             f'Unity ActionData：{action_count}；NPC 引用动作去重：{len(used)}；表情及对应动作：{len(gestures)}。')
        line('\nUnity `AssetGuid.Value`（或旧格式 `Identifier.Guid`）与 `quantumDatabase.bin` 主记录 `Guid.Value` 对齐。'
             '以下为已解析的配置引用，不保证实战触发；未模拟等级、多人、特质、静音或运行时条件。')
        line('未扫描场景、运行库独立大对象块或原生代码注入；不把跨 NPC 召唤、AOE 实体或双人动作配置当作该 NPC 的 ActionData。')
        line('沃里克的 `Multiplayer` 组引用延迟震波与快速跳劈；`Common` 组引用的是 `mutantSoldierAttackOverheadShortData`。'
             '`mutantSoldierAttackOverheadShortAOEData` 只核对了资产 GUID，没有可证实的归属链。')
        line('\n## 实际引用链（去掉数组下标）\n')
        for chain in sorted(chains):
            line(f'- `{chain}`')
        line('\n## 自检和未覆盖项\n')
        for _, check in checks:
            line(f'- {check}')
        for path, fragment in skips:
            line(f'- 无 GUID，未纳入：`{path}`，类型树片段 `{fragment}`')
        for problem in sorted(problems):
            line(f'- {problem}')
        line('\n## NPC\n')
        for row in enemies:
            line(f'### {label(row)} — {row["EnemyType"]}')
            line(f'- 资产：`{row["path"]}`；类：`{row["class"]}`')
            line(f'- AI：{label(row["controller"]) if row["controller"] else "无直接引用"}')
            if not row['actions']:
                line('- 没有解析到 ActionData 引用。')
            for action in row['actions']:
                line(f'- {label(action)} (`{action["class"]}`)')
                for source in action['sources']:
                    parts = [source['kind']]
                    if source.get('group'):
                        parts.append(f'组：{source["group"]}')
                    if source.get('selection'):
                        parts.append(f'选项：{source["selection"]}')
                    if source.get('muted'):
                        parts.append('Mute')
                    if source.get('solo'):
                        parts.append('Solo')
                    if source.get('min_level') is not None:
                        parts.append(f'等级 ≥ {source["min_level"]}')
                    if source.get('required_affix_guid'):
                        parts.append(f'特质 GUID {source["required_affix_guid"]}')
                    line(f'  - {"；".join(parts)}：`{" → ".join(source["chain"])}`')
            if row['related_paired_actions_not_action_data']:
                line('- 双人动作配置（非 ActionData）：' + '、'.join(map(label, row['related_paired_actions_not_action_data'])))
            line()
    print(f'assets.tsv lines={len(rows)+1} classes={len(counts)}; top 15:', counts.most_common(15))
    print(f'NpcData={len(enemies)} with actions={sum(bool(n["actions"]) for n in enemies)} '
          f'ActionData assets={action_count} distinct NPC actions={len(used)} gestures rows={len(gestures)}')
    print(f'Sources: {dict(coverage)}; skipped without GUID: {len(skips)}; unresolved notes: {len(problems)}')
    for note in sorted(problems)[:12]:
        print('NOTE', note)
    print(f'Elapsed: {perf_counter()-started:.2f}s')


if __name__ == '__main__':
    main()
