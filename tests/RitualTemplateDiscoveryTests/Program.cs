using CrucibleUnlock;
using Il2CppMoon.Forsaken;
using UnityEngine;

int passed = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception(name);
    ++passed;
}
void Throws(Action action, string message)
{
    try { action(); }
    catch (InvalidOperationException error) when (error.Message == message) { ++passed; return; }
    throw new Exception("Expected: " + message);
}
GameObject Child(GameObject parent, bool active = true)
{
    var child = new GameObject { activeSelf = active };
    parent.transform.AddChild(child);
    return child;
}
GeneralInteractableView Bowl(GameObject node, string name = "firstStatueBowlInteraction",
    long guid = RitualViewPolicy.BowlDataGuid, bool bound = true)
{
    var bowl = node.Add(new GeneralInteractableView { IsBoundToEntity = bound });
    bowl.Data.Name = name;
    bowl.DataAsset.Id.Value = guid;
    return bowl;
}
InteractionView View(GameObject node, long guid = TrialRepairPolicy.OfferingGuid)
    => node.Add(new InteractionView { QuantumAsset = new QuantumAsset { AssetGuid = new GuidValue { Value = guid } } });
bool Allowed(int count) => RitualViewPolicy.ShouldCreate(TrialRepairPolicy.OfferingGuid,
    RitualViewPolicy.BowlDataGuid, "secondStatueBowlInteraction", true, true, count, true);

// A bowl and view can both be on the traversal root.
var root = new GameObject();
var first = Bowl(root);
var template = View(root);
var found = RitualViewRepair.Discover(root.transform);
Check(found.Count == 1 && ReferenceEquals(found.First, first) && ReferenceEquals(found.Template, template), "root match");
Check(Allowed(found.Count), "single match accepted by actual policy");

root = new GameObject();
Bowl(Child(root), "otherBowl");
View(root.transform.GetChild(0).gameObject);
var wrongGuidBowl = Child(root);
Bowl(wrongGuidBowl, guid: 1);
View(wrongGuidBowl);
var unbound = Child(root);
Bowl(unbound, bound: false);
View(unbound);
var wrongView = Child(root);
Bowl(wrongView);
View(wrongView, 2);
found = RitualViewRepair.Discover(root.transform);
Check(found.Count == 0 && found.First == null && found.Template == null, "name, bowl GUID, binding, and view GUID excluded");
Check(!Allowed(found.Count), "zero matches rejected");
var inactive = Child(root, active: false);
first = Bowl(inactive);
var inactiveView = Child(Child(inactive, active: false), active: false);
template = View(inactiveView);
found = RitualViewRepair.Discover(root.transform);
Check(found.Count == 1 && ReferenceEquals(found.First, first) && ReferenceEquals(found.Template, template), "inactive descendants included");

root = new GameObject();
first = Bowl(Child(root));
View(first.transform.gameObject);
View(Child(first.transform.gameObject));
found = RitualViewRepair.Discover(root.transform);
Check(found.Count == 2, "two views counted");
Check(!Allowed(found.Count), "duplicate views rejected");

root = new GameObject();
var outer = Bowl(Child(root));
View(outer.transform.gameObject);
var inner = Bowl(Child(outer.transform.gameObject));
template = View(inner.transform.gameObject);
found = RitualViewRepair.Discover(root.transform);
Check(found.Count == 3 && ReferenceEquals(found.First, inner) && ReferenceEquals(found.Template, template),
    "nested bowl view counted once for each enclosing bowl");
Check(!Allowed(found.Count), "nested matches rejected");

// Exactly 4096 Transform nodes is allowed. The next node fails the traversal cap.
root = new GameObject();
var tail = root;
for (int i = 1; i < 4096; ++i) tail = Child(tail);
first = Bowl(tail);
template = View(tail);
found = RitualViewRepair.Discover(root.transform);
Check(found.Count == 1 && ReferenceEquals(found.First, first), "4096 statue nodes allowed");
Child(tail);
Throws(() => RitualViewRepair.Discover(root.transform), "Invalid statue hierarchy");

root = new GameObject();
first = Bowl(root);
tail = root;
for (int i = 1; i < 4096; ++i) tail = Child(tail);
View(tail);
found = RitualViewRepair.Discover(root.transform);
Check(found.Count == 1, "4096 offering nodes allowed");
Child(tail);
Throws(() => RitualViewRepair.Discover(root.transform), "Invalid offering hierarchy");

root = new GameObject();
root.ComponentFailure = new InvalidOperationException("component failure");
Throws(() => RitualViewRepair.Discover(root.transform), "component failure");
root = new GameObject();
first = Bowl(root);
Child(root).ComponentFailure = new InvalidOperationException("view component failure");
Throws(() => RitualViewRepair.Discover(root.transform), "view component failure");

Console.WriteLine($"Ritual template discovery tests: {passed} passed");
