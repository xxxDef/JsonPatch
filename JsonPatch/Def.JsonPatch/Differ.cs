using System.Collections;
using System.Reflection;

namespace Def.JsonPatch
{
    public class Differ
    {
        private readonly DifferStrategies strategies;

        public Differ(DifferStrategies strategies)
        {
            this.strategies = strategies;
        }

        // compare current and new results, apply new results to current and return changes
        public IEnumerable<Change> DiffAndPatch<TCurrent, TNew>(TCurrent? current, TNew changed)
            where TCurrent : class
            where TNew : class
        {
            if (current == null)
            {
                // in this case we fully replace all content of object - use https://json8.github.io/patch/demos/apply/ to test
                return new Change[]
                {
                    new Change
                    {
                        op = Operations.replace,
                        path = "",
                        value = changed
                    }
                };
            }
            return DiffAndPatch(current, changed, "");
        }

        record ValueBinding(object Container, PropertyInfo Property, object? Value);

        ValueBinding GetBinding(object container, PropertyInfo property)
        {
            if (property.DeclaringType == container.GetType())
            {
                var value = strategies.GetValue(property, container);
                return new ValueBinding(container, property, value);
            }
            else
            {
                var sameProperty = strategies.GetSameProperty(container, property);
                var value = strategies.GetValue(sameProperty, container);
                return new ValueBinding(container, sameProperty, value);
            }
        }

        private IEnumerable<Change> DiffAndPatch(object original, object changed, string parentPath)
        {
            var props = strategies.GetProperties(changed);

            foreach (var prop in props)
            {
                if (strategies.Skip != null && strategies.Skip(prop))
                    continue;

                var changedBinding = GetBinding(changed, prop);
                var originalBinding = GetBinding(original, prop);
                string curPath = $"{parentPath}/{prop.Name}";

                if (prop.IsSimpleTypeOrString())
                {
                    foreach (var c in DiffAndPatchValue(originalBinding, changedBinding, curPath))
                        yield return c;
                }
                else if (prop.IsAssociativeDictionary())
                {
                    foreach (var c in DiffAndPatchDictionary(originalBinding, changedBinding, curPath))
                        yield return c;
                }
                else if (prop.IsEnumerable())
                {
                    foreach (var c in DiffAndPatchCollection(originalBinding, changedBinding, curPath))
                        yield return c;
                }
                else
                {
                    foreach (var c in DiffAndPatchContainer(originalBinding, changedBinding, curPath))
                        yield return c;
                }
            }
        }

        private IEnumerable<Change> DiffAndPatchValue(ValueBinding original, ValueBinding changed, string path)
        {
            if (strategies.AreEquals(original.Value, changed.Value))
                yield break;

            if (strategies.SetValue(original.Property, original.Container, changed.Value))
                yield break;

            yield return new Change
            {
                path = path,
                op = Operations.replace,
                value = changed.Value
            };
        }

        private IEnumerable<Change> DiffAndPatchContainer(ValueBinding original, ValueBinding changed, string currentpatch)
        {
            if (original.Value == null && changed.Value == null) // do nothing if both view and model are null
                yield break;

            if (original.Value != null && changed.Value == null)
            {
                // remove old 
                strategies.SetValue(original.Property, original.Container, null);
                yield return Change.Remove(currentpatch);
            }
            else if (original.Value == null && changed.Value != null)
            {
                //add new 
                var newCurrentChild = strategies.Create(original.Property.PropertyType);
                Guards.InternalErrorIfNull(newCurrentChild, $"failed to create new item of type {original.Property.PropertyType}");
                strategies.SetValue(original.Property, original.Container, newCurrentChild);
                var skipped = DiffAndPatch(newCurrentChild, changed.Value, currentpatch).ToArray();

                yield return Change.Add(currentpatch, original.Value);
            }
            else if (original.Value != null && changed.Value != null)
            {
                // update/replace
                foreach (var c in DiffAndPatch(original.Value, changed.Value, currentpatch))
                    yield return c;
            }
        }



        private IEnumerable<Change> DiffAndPatchDictionary(
            ValueBinding original, ValueBinding changed, string currentPatch)
        {
            Guards.InternalErrorIfFalse(original.Property.IsAssociativeDictionary(),
               $"Field {original.Property.Name} should be dictionary");

            var originalDict = original.Value as IDictionary<string, object>;
            var changedDict = changed.Value as IDictionary<string, object>;

            Guards.InternalErrorIfNull(originalDict,
               $"Dictionary {original.Property.Name} should be initialized in current object");

            if (changedDict == null && originalDict == null)
                yield break;

            if (changedDict == null && originalDict != null)
            {
                // clear original dict
                foreach (var item in originalDict)
                {
                    var curpatch = $"{currentPatch}/{item.Key}";
                    yield return Change.Remove(curpatch);
                }
                originalDict.Clear();
                yield break;
            }

            Guards.InternalErrorIfFalse(changedDict != null && originalDict != null, $"Unexpected case, both dictionaries should be not null in this place");

            foreach (var curKey in originalDict.Keys)
            {
                var curpatch = $"{currentPatch}/{curKey}";
                if (!changedDict.TryGetValue(curKey, out var changedItem) || changedItem == null)
                {
                    // no this item in new dict, remove it
                    originalDict.Remove(curKey);
                    yield return Change.Remove(curpatch);
                }
            }

            // add new and update exsising
            foreach (var changedItem in changedDict)
            {
                var curpatch = $"{currentPatch}/{changedItem.Key}";

                if (!originalDict.TryGetValue(changedItem.Key, out var origItem))
                {
                    if (changedItem.Value != null)
                    {
                        // new item, add it
                        var newItem = strategies.CreateFrom(changedItem.Value);
                        Guards.InternalErrorIfNull(newItem);
                        var skipped = DiffAndPatch(newItem, changedItem.Value, curpatch).ToArray();

                        originalDict.Add(changedItem.Key, newItem);
                        yield return Change.Add(curpatch, newItem);
                    }
                }
                else
                {
                    if (changedItem.Value == null)
                    {
                        // new item is null, remove old item
                        if (origItem != null)
                        {
                            originalDict.Remove(changedItem.Key);
                            yield return Change.Remove(curpatch);
                        }
                    }
                    else if (changedItem.GetType().IsSimpleTypeOrString())
                    {
                        // both items exsist and they are simple
                        if (!strategies.AreEquals(origItem, changedItem.Value))
                        {
                            originalDict[changedItem.Key] = changedItem.Value;
                            yield return Change.Replace(curpatch, changedItem.Value);
                        };
                    }
                    else
                    {
                        // both items exsist, value is complex
                        foreach (var c in DiffAndPatch(origItem, changedItem.Value, curpatch))
                            yield return c;
                    }
                }
            }
        }



        private IEnumerable<Change> DiffAndPatchCollection(
            ValueBinding original, ValueBinding changed, string currentPatch)
        {
            var currentColl = original.Value as IList;
            Guards.InternalErrorIfNull(currentColl,
                $"Collection {original.Property.Name} should implement IList interface");

            var changedColl = changed.Value as IEnumerable;

            if (changedColl == null)
                return RemoveAllCollectionItems(currentColl, currentPatch);
            else
                return DiffAndPatchCollection(currentColl, changedColl, currentPatch);
        }

        private IEnumerable<Change> RemoveAllCollectionItems(IList coll, string currentPatch)
        {
            for (int i = 0; i < coll.Count;)
            {
                coll.RemoveAt(i);
                var curpatch = $"{currentPatch}/{i}";
                yield return Change.Remove(curpatch);
            }
        }

        record OldNewPair(object Changed)
        {
            internal object? Original { get; set; }
        }

        private IEnumerable<Change> DiffAndPatchCollection(
            IList originalCollection,
            IEnumerable newCollection,
            string path)
        {
            var oldNewPairs = new List<OldNewPair>();
            foreach (var m in newCollection)
            {
                Guards.InternalErrorIfNull(m, $"unexpected null item in collection of type {newCollection.GetType()}");
                oldNewPairs.Add(new OldNewPair(m));
            }

            // First iteration: bind current items to new items and remove missed from current items
            for (int i = 0; i < originalCollection.Count;)
            {
                var view = originalCollection[i];
                Guards.InternalErrorIfNull(view, $"Unexpected null item in position {i} in collection {originalCollection.GetType()}");

                var old = oldNewPairs.FirstOrDefault(mv => mv.Changed != null && strategies.AreSame(mv.Changed, view));
                if (old != null)
                {
                    Guards.InternalErrorIfFalse(old.Original == null, $"Object in old collection for type {view.GetType()} exist more than once.");
                    old.Original = view;
                    ++i;
                }
                else
                {
                    // current item is not exsist in changed collection - remove it
                    originalCollection.RemoveAt(i);
                    var curpatch = $"{path}/{i}";
                    yield return Change.Remove(curpatch);
                }
            }

            Guards.InternalErrorIfFalse(originalCollection.Count <= oldNewPairs.Count, "currentColl have to contains only items already existing in changedColl");

            // add new items - items which are present only in newCollection
            for (int pos = 0; pos < oldNewPairs.Count; pos++)
            {
                var pair = oldNewPairs[pos];
                if (pair.Original != null)
                    continue;

                // TODO: optimize 
                Guards.InternalErrorIfFalse(originalCollection.Count >= pos,
                    $"on this step of DiffAndPatchCollection we expect original colection has more elements than current element in new collection to allow correctly insert new element");

                pair.Original = strategies.CreateFrom(pair.Changed);
                originalCollection.Insert(pos, pair.Original);

                var curpatch = $"{path}/{pos}";

                // update current from changed without registry changes in result 
                // this initialized object will be set into value
                if (!pair.Original.GetType().IsSimpleTypeOrString())
                {
                    var skipped = DiffAndPatch(pair.Original, pair.Changed, curpatch).ToArray();
                }

                yield return Change.Add(curpatch, pair.Original);
            }

            Guards.InternalErrorIfFalse(originalCollection.Count == oldNewPairs.Count, "viewColl have to contains same items as in modelColl");

            // optimization: detect what way (from top to bottom or bottom to top) will have less movings
            int weight = 0;
            for (int pos = 0; pos < oldNewPairs.Count; pos++)
            {
                var pair = oldNewPairs[pos];
                var viewPos = originalCollection.IndexOf(pair.Original);

                if (viewPos < pos)
                    --weight;
                else if (viewPos > pos)
                    ++weight;
            }

            if (weight <= 0)
            {
                for (int p = 0; p < oldNewPairs.Count; p++)
                    foreach (var c in MoveAndUpdate(p))
                        yield return c;
            }
            else
            {
                for (int p = oldNewPairs.Count; p > 0; p--)
                    foreach (var c in MoveAndUpdate(p - 1))
                        yield return c;
            }
            // move and update

            IEnumerable<Change> MoveAndUpdate(int pos)
            {
                var pair = oldNewPairs[pos];
                Guards.InternalErrorIfNull(pair.Changed, $"All views should already created and bound with model");
                Guards.InternalErrorIfNull(pair.Original, "All views should already created and bound with model");

                var oldPos = originalCollection.IndexOf(pair.Original);

                var curpatch = $"{path}/{pos}";
                if (oldPos != pos)
                {
                    var fromPath = $"{path}/{oldPos}";
                    originalCollection.RemoveAt(oldPos);
                    originalCollection.Insert(pos, pair.Original);

                    yield return Change.Move(curpatch, fromPath);
                }

                if (!pair.Original.GetType().IsSimpleTypeOrString())
                {
                    foreach (var c in DiffAndPatch(pair.Original, pair.Changed, curpatch))
                        yield return c;
                }
            }
        }


    }
}