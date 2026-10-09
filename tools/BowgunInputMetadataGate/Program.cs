using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Reflection.Emit;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;

// Inspect PE metadata only. Never load an inspected assembly or game DLL.
int checks=0;
void Check(bool ok,string label) { ++checks; if (!ok) throw new InvalidOperationException(label); }
string TypeName(MetadataReader r,EntityHandle h) => h.Kind switch {
    HandleKind.TypeReference => Join(r,r.GetTypeReference((TypeReferenceHandle)h).Namespace,r.GetTypeReference((TypeReferenceHandle)h).Name),
    HandleKind.TypeDefinition => Join(r,r.GetTypeDefinition((TypeDefinitionHandle)h).Namespace,r.GetTypeDefinition((TypeDefinitionHandle)h).Name),
    _ => throw new InvalidOperationException("Unexpected type handle")
};
string Join(MetadataReader r,StringHandle ns,StringHandle name) => r.GetString(ns)+"."+r.GetString(name);
string SignatureType(MetadataReader r,ref BlobReader b) => b.ReadByte() switch {
    0x01 => "Void", 0x02 => "Boolean", 0x08 => "Int32", 0x0a => "Int64",
    0x10 => SignatureType(r,ref b)+"&",
    0x11 or 0x12 => TypeName(r,b.ReadTypeHandle()),
    _ => throw new InvalidOperationException("Unexpected signature element")
};
TypeDefinition Find(MetadataReader r,string name) => r.GetTypeDefinition(r.TypeDefinitions.Single(h=>TypeName(r,h)==name));
void Method(MetadataReader r,TypeDefinition t,string name,string result,params string[] parameters)
{
    var m=r.GetMethodDefinition(t.GetMethods().Single(h=>r.GetString(r.GetMethodDefinition(h).Name)==name));
    var blob=r.GetBlobReader(m.Signature);
    var header=blob.ReadSignatureHeader();
    int count=blob.ReadCompressedInteger();
    string returns=SignatureType(r,ref blob);
    var p=Enumerable.Range(0,count).Select(_=>SignatureType(r,ref blob)).ToArray();
    Check(!header.IsInstance && (m.Attributes&MethodAttributes.Static)!=0 && m.RelativeVirtualAddress!=0 &&
        returns==result && p.SequenceEqual(parameters),name+" exact managed callback signature/body");
}
using var baseline=File.OpenRead(args[0]);
using var baselinePe=new PEReader(baseline);
var core=baselinePe.GetMetadataReader();
var type=Find(core,"CrucibleUnlock.BowgunInputRepair");
foreach (var (name,wanted) in new[]{("_enabled","Boolean"),("_boundHeroRaw","Int64"),("_captureUntil","Int64"),("_lastRoutedModifiers","Int32"),("_suppressHudBlock","Boolean")})
{
    var field=core.GetFieldDefinition(type.GetFields().Single(h=>core.GetString(core.GetFieldDefinition(h).Name)==name));
    var b=core.GetBlobReader(field.Signature);
    b.ReadSignatureHeader();
    Check(SignatureType(core,ref b)==wanted && (field.Attributes&FieldAttributes.Static)!=0 &&
        (field.Attributes&FieldAttributes.InitOnly)==0,name+" writable static CLR field");
    if (name!="_suppressHudBlock") continue;
    bool threadStatic=field.GetCustomAttributes().Any(h=> {
        var a=core.GetCustomAttribute(h);
        return a.Constructor.Kind==HandleKind.MemberReference &&
            TypeName(core,core.GetMemberReference((MemberReferenceHandle)a.Constructor).Parent)=="System.ThreadStaticAttribute";
    });
    Check(threadStatic,"HUD scope is thread static in the actual installed baseline");
}
Method(core,type,"AfterInput","Void","Il2CppMoon.Forsaken.QuantumLocalInputSource","Il2CppQuantum.Frame");
Method(core,type,"AfterHudButton","Void","Il2CppMoon.Forsaken.RevisedInput","Int32","Boolean&");
Method(core,Find(core,"CrucibleUnlock.BowgunInputPolicy"),"ShotModifiers","Int32","Int32");
using var candidate=File.OpenRead(args[1]);
using var candidatePe=new PEReader(candidate);
var mod=candidatePe.GetMetadataReader();
var extension=Find(mod,"BowgunRepair.BlockInputRouting");
Method(mod,extension,"BeforeInput","Boolean","Il2CppMoon.Forsaken.QuantumLocalInputSource","Il2CppQuantum.Frame");
Method(mod,extension,"BeforeHudButton","Boolean","Il2CppMoon.Forsaken.RevisedInput","Int32","Boolean&");
// This native callback can run on the simulation input thread. Follow its
// managed helper calls and reject the view APIs proven main-thread-only by the
// captured 0.3.2 stack. Merely having a valid native signature is insufficient.
var root=extension.GetMethods().Single(h=>mod.GetString(mod.GetMethodDefinition(h).Name)=="BeforeInput");
var pending=new Stack<MethodDefinitionHandle>(); pending.Push(root);
var seen=new HashSet<MethodDefinitionHandle>();
var blocked=new HashSet<string>();
var opcodes=typeof(OpCodes).GetFields().Where(f=>f.FieldType==typeof(OpCode))
    .Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(c=>unchecked((ushort)c.Value));
while (pending.Count>0)
{
    var handle=pending.Pop();
    if (!seen.Add(handle)) continue;
    var definition=mod.GetMethodDefinition(handle);
    if (definition.RelativeVirtualAddress==0) continue;
    var il=candidatePe.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes() ?? throw new InvalidOperationException("IL bytes absent");
    for (int offset=0;offset<il.Length;)
    {
        ushort key=il[offset++]; if (key==0xfe) key=(ushort)(0xfe00|il[offset++]);
        var opcode=opcodes[key];
        int size=opcode.OperandType switch {
            OperandType.InlineNone=>0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar=>1,
            OperandType.InlineVar=>2,
            OperandType.InlineI8 or OperandType.InlineR=>8,
            OperandType.InlineSwitch=>4+4*BitConverter.ToInt32(il,offset),
            _=>4
        };
        if (opcode.OperandType==OperandType.InlineMethod)
        {
            EntityHandle target=MetadataTokens.EntityHandle(BitConverter.ToInt32(il,offset));
            if (target.Kind==HandleKind.MethodSpecification) target=mod.GetMethodSpecification((MethodSpecificationHandle)target).Method;
            if (target.Kind==HandleKind.MethodDefinition) pending.Push((MethodDefinitionHandle)target);
            if (target.Kind==HandleKind.MemberReference)
            {
                var member=mod.GetMemberReference((MemberReferenceHandle)target);
                string owner=member.Parent.Kind is HandleKind.TypeReference or HandleKind.TypeDefinition ? TypeName(mod,member.Parent) : "<generic>";
                string name=mod.GetString(member.Name);
                if (owner is "Il2CppMoon.TheLoop.ViewFrame" or "Il2CppMoon.ViewSynchronization.Player.PlayerViewsAPI" ||
                    (owner=="Il2CppQuantum.QuantumGame" && name=="GetFramesView")) blocked.Add(owner+"::"+name);
            }
        }
        offset+=size;
    }
}
Check(blocked.Count==0,"Input callback reaches main-thread view API: "+string.Join(", ",blocked));
Console.WriteLine($"{checks} input CLR metadata checks; 0 failures");
foreach (var path in args) Console.WriteLine(Path.GetFileName(path)+" SHA256 "+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
