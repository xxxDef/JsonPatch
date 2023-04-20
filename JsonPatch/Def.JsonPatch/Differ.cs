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

        private IEnumerable<Change> DiffAndPatch(object current, object changed, string parentPath)
        {
            var props = strategies.GetProperties(changed); 
            foreach (var prop in props)
            {
                if (strategies.Skip != null && strategies.Skip(prop))
                    continue;

                string curPath = $"{parentPath}/{prop.Name}";

                if (prop.IsSimpleTypeOrString())
                {
                    foreach (var c in DiffAndPatchValue(current, prop, changed, curPath))
                        yield return c;
                }
                else if (prop.IsAssociativeDictionary())
                {
                    foreach (var c in DiffAndPatchDictionary(current, prop, changed, curPath))
                        yield return c;
                }
                else if (prop.IsEnumerable())
                {
                    foreach (var c in DiffAndPatchCollection(current, prop, changed, curPath))
                        yield return c;
                }
                else
                {
                    foreach (var c in DiffAndPatchContainer(current, prop, changed, curPath))
                        yield return c;
                }
            }
        }

        private IEnumerable<Change> DiffAndPatchValue(object current, PropertyInfo prop, object changed, string curPath)
        {
            var newValue = strategies.GetValue(prop, changed);
            var currentProp = strategies.GetSameProperty(current, prop);
            var oldValue = strategies.GetValue(currentProp, current);

            if (!strategies.AreEquals(oldValue, newValue))
            {
                if (strategies.SetValue(currentProp, current, newValue))
                {
                    yield return new Change
                    {
                        path = curPath,
                        op = Operations.replace,
                        value = newValue
                    };
                }
            }
        }

        private IEnumerable<Change> DiffAndPatchContainer(object current, PropertyInfo prop, object changed, string currentpatch)
        {
            var currentProp = strategies.GetSameProperty(current, prop);
            var currentChild = strategies.GetValue(currentProp, current);
            var changedChild = strategies.GetValue(prop, changed);

            if (currentChild == null && changedChild == null) // do nothing if both view and model are null
                yield break;

            if (currentChild != null && changedChild == null)
            {
                strategies.SetValue(prop, current, null);
                // remove old 
                yield return new Change
                {
                    op = Operations.remove,
                    path = currentpatch
                };
            }
            else if (currentChild == null && changedChild != null)
            {
                //add new 
                var newCurrentChild = Activator.CreateInstance(prop.PropertyType);
                Guards.InternalErrorIfNull(newCurrentChild, $"failed to create new item of type {prop.PropertyType}");
                strategies.SetValue(prop, current, newCurrentChild);
                var skipped = DiffAndPatch(newCurrentChild, changedChild, currentpatch).ToArray();

                yield return new Change
                {
                    op = Operations.add,
                    path = currentpatch,
                    value = currentChild
                };
            }
            else if (currentChild != null && changedChild != null)
            {
                foreach (var c in DiffAndPatch(currentChild, changedChild, currentpatch))
                    yield return c;
            }
        }

        record OldNewPair(object NewItem)
        {
            internal object? CurrentItem { get; set; }
        }

        private IEnumerable<Change> DiffAndPatchDictionary(
            object current, PropertyInfo prop, object changed, string currentpatch)
        {
            var propInCurrent = strategies.GetSameProperty(current, prop);

            Guards.InternalErrorIfFalse(propInCurrent.IsAssociativeDictionary(),
               $"Field {propInCurrent.Name} should be dictionary");

            var currentDict = strategies.GetValue(propInCurrent, current) as IDictionary<string, object>;
            var changedDict = strategies.GetValue(propInCurrent, changed) as IDictionary<string, object>;

            Guards.InternalErrorIfNull(currentDict,
               $"Dictionary {propInCurrent.Name} should be initialized in current object");

            if (changedDict == null && currentDict == null)
                yield break;

            if (changedDict == null && currentDict != null)
            {
                foreach (var item in currentDict)
                {
                    var curpatch = $"{currentpatch}/{item.Key}";
                    yield return new Change
                    {
                        op = Operations.remove,
                        path = curpatch
                    };
                }
                currentDict.Clear();
                yield break;
            }

            Guards.InternalErrorIfFalse(changedDict != null && currentDict != null, $"Unexpected case, both dictionaries should be not null in this place");

            foreach (var curKey in currentDict.Keys)
            {
                var curpatch = $"{currentpatch}/{curKey}";
                if (!changedDict.TryGetValue(curKey, out var changedItem) || changedItem == null)
                {
                    // no this item in new dict, remove it
                    currentDict.Remove(curKey);
                    yield return new Change
                    {
                        op = Operations.remove,
                        path = curpatch
                    };
                }
            }

            // add new and update exsising
            foreach (var changedItem in changedDict)
            {
                var curpatch = $"{currentpatch}/{changedItem.Key}";

                if (!currentDict.TryGetValue(changedItem.Key, out var curItem))
                {
                    if (changedItem.Value != null)
                    {
                        // new item, add it
                        if (changedItem.Value.GetType().IsSimpleTypeOrString())
                        {
                            curItem = changedItem.Value;
                        }
                        else
                        {
                            curItem = Activator.CreateInstance(changedItem.Value.GetType());
                            Guards.InternalErrorIfNull(curItem);
                            var skipped = DiffAndPatch(curItem, changedItem.Value, curpatch).ToArray();
                        }
                        currentDict.Add(changedItem.Key, curItem);
                        yield return new Change
                        {
                            path = curpatch,
                            op = Operations.add,
                            value = curItem
                        };
                    }
                }
                else
                {
                    if (changedItem.Value == null)
                    {
                        // new item is null, remove old item
                        if (curItem != null)
                        {
                            currentDict.Remove(changedItem.Key);
                            yield return new Change
                            {
                                path = curpatch,
                                op = Operations.remove
                            };
                        }
                    }
                    else if (!changedItem.GetType().IsSimpleTypeOrString())
                    {
                        // both items exsist, value is complex
                        foreach (var c in DiffAndPatch(curItem, changedItem.Value, curpatch))
                            yield return c;
                    }
                    else
                    {
                        // both items exsist
                        if (!strategies.AreEquals(curItem, changedItem.Value))
                        {
                            currentDict[changedItem.Key] = changedItem.Value;
                            yield return new Change
                            {
                                op = Operations.replace,
                                path = curpatch,
                                value = changedItem.Value
                            };
                        }
                    }
                }
            }

        }

        private IEnumerable<Change> DiffAndPatchCollection(
            object current,
            PropertyInfo prop,
            object changed,
            string currentpatch)
        {
            var propInCurrent = strategies.GetSameProperty(current, prop);
            var rawCurrentColl = strategies.GetValue(propInCurrent, current);
            Guards.InternalErrorIfFalse(rawCurrentColl is IList,
                $"Collection {propInCurrent.Name} should implement IList interface");

            var currentColl = (IList)rawCurrentColl;

            var itemType = ReflectionEx.GetItemType(currentColl);

            var rawChangedColl = strategies.GetValue(prop, changed);

            if (itemType.IsString())
            {
                var changedColl = rawChangedColl as IEnumerable;

                foreach (var c in DiffAndPatchCollection(currentColl, changedColl, currentpatch,
                    x => x?.ToString(),
                    x => x))
                {
                    yield return c;
                }

            }
            else
            {
                var changedColl = rawChangedColl as IEnumerable;
                foreach (var c in DiffAndPatchCollection(currentColl, changedColl, currentpatch,
                    strategies.GetUniqueId,
                    (x) =>
                    {
                        var newObj = strategies.Create(x.GetType());
                        strategies.SetUniqueId(newObj, x);
                        return newObj;
                    }))
                {
                    yield return c;
                }
            }
        }

        private IEnumerable<Change> DiffAndPatchCollection(
            IList oldCollection,
            IEnumerable? newCollection,
            string currentpatch,
            Func<object, string?> getUniquieId,
            Func<object, object> createFrom)
        {
            if (newCollection == null)
            {
                if (oldCollection != null)
                {
                    for (int i = 0; i < oldCollection.Count;)
                    {
                        var view = oldCollection[i];
                        oldCollection.RemoveAt(i);
                        var curpatch = $"{currentpatch}/{i}";
                        yield return new Change
                        {
                            op = Operations.remove,
                            path = curpatch
                        };
                    }
                }
                yield break;
            }

            if (oldCollection == null) // all collections should be created
                Guards.InternalErrorIfNotImplemented($"collection used in json patch by path {currentpatch} should be initialized");

            var oldNewPairs = new List<OldNewPair>();
            foreach (var m in newCollection)
            {
                Guards.InternalErrorIfNull(m, $"unexpected null item in collection of type {newCollection.GetType()}");
                oldNewPairs.Add(new OldNewPair(m));
            }

            // First iteration: bind old items to new items and remove missed from old items
            for (int i = 0; i < oldCollection.Count;)
            {
                var view = oldCollection[i];
                Guards.InternalErrorIfNull(view, $"unexpected null item in collection {oldCollection.GetType()}");

                var oldId = getUniquieId(view);
                var old = oldNewPairs.FirstOrDefault(mv => mv.NewItem != null && getUniquieId(mv.NewItem) == oldId);
                if (old != null)
                {
                    Guards.InternalErrorIfFalse(old.CurrentItem == null, $"Id '{oldId} in old collection for type {view.GetType()} exist more than once.");
                    old.CurrentItem = view;
                    ++i;
                }
                else
                {
                    // current item is not exsist in changed collection - remove it
                    oldCollection.RemoveAt(i);
                    var curpatch = $"{currentpatch}/{i}";
                    yield return new Change
                    {
                        op = Operations.remove,
                        path = curpatch
                    };
                }
            }

            Guards.InternalErrorIfFalse(oldCollection.Count <= oldNewPairs.Count, "currentColl have to contains only items already existing in changedColl");

            // add new items - items which are present only in newCollection
            for (int pos = 0; pos < oldNewPairs.Count; pos++)
            {
                var pair = oldNewPairs[pos];
                if (pair.CurrentItem == null)
                {
                    // TODO: optimize 
                    Guards.InternalErrorIfFalse(oldCollection.Count >= pos,
                        $"on this step of DiffAndPatchCollection we expect old colection has more elements than current element in new collection");

                    pair.CurrentItem = createFrom(pair.NewItem);
                    oldCollection.Insert(pos, pair.CurrentItem);

                    var curpatch = $"{currentpatch}/{pos}";

                    // update current from changed without registry changes in result (it is why new List<Change> is used)
                    // this initialized view will be set into value
                    if (!pair.CurrentItem.GetType().IsSimpleTypeOrString())
                    {
                        var sipped = DiffAndPatch(pair.CurrentItem, pair.NewItem, curpatch).ToArray();
                    }

                    yield return new Change
                    {
                        op = Operations.add,
                        path = curpatch,
                        value = pair.CurrentItem
                    };
                }
            }

            Guards.InternalErrorIfFalse(oldCollection.Count == oldNewPairs.Count, "viewColl have to contains same items as in modelColl");

            // optimization: detect what way (from top to bottom or bottom to top) will have less movings
            int weight = 0;
            for (int pos = 0; pos < oldNewPairs.Count; pos++)
            {
                var pair = oldNewPairs[pos];
                var viewPos = oldCollection.IndexOf(pair.CurrentItem);

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
                Guards.InternalErrorIfNull(pair.NewItem, $"All views should already created and bound with model");
                Guards.InternalErrorIfNull(pair.CurrentItem, "All views should already created and bound with model");

                var oldPos = oldCollection.IndexOf(pair.CurrentItem);

                var curpatch = $"{currentpatch}/{pos}";
                if (oldPos != pos)
                {
                    var fromPath = $"{currentpatch}/{oldPos}";
                    oldCollection.RemoveAt(oldPos);
                    oldCollection.Insert(pos, pair.CurrentItem);

                    yield return new Change
                    {
                        op = Operations.move,
                        path = curpatch,
                        from = fromPath
                    };
                }

                if (!pair.CurrentItem.GetType().IsSimpleTypeOrString())
                {
                    foreach (var c in DiffAndPatch(pair.CurrentItem, pair.NewItem, curpatch))
                        yield return c;
                }
            }
        }
    }
}