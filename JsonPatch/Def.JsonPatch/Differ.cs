using System.Collections;
using System.Reflection;

namespace Def.JsonPatch
{
    public class Differ
    {
        public Func<PropertyInfo, bool>? SkipStrategy { get; set; }

        public Func<object, string?> GetUniqueIdStrategy { get; set; } = (obj) =>
        {
            throw new NotImplementedException($"GetUniqueIdStrategy not set");
        };

        public Action<object, object> SetUniqueIdStrategy { get; set; } = (from, to) =>
        {
            throw new NotImplementedException($"SetUniqueIdStrategy not set");
        };

        public Func<Type, object> CreateStrategy { get; set; } = (type) =>
        {
            var obj = Activator.CreateInstance(type);
            Guards.InternalErrorIfNull(obj);
            return obj;
        };

        public Func<PropertyInfo, object, object?, bool> SetValueStrategy { get; set; } = (pi, obj, newValue) =>
        {
            pi.SetValue(obj, newValue);
            return true;
        };

        public Func<object?, object?, bool> AreEqualsStrategy { get; set; } = (x, y) =>
        {
            if (x == null && y == null)
                return true;
            if (x == null || y == null)
                return false;
            if (x.GetType() != y.GetType())
                return false;
            if (x is string xs && y is string ys) // or y is string
                return xs == ys;
            if (x is IStructuralEquatable ex)
                return ex.Equals(y, StructuralComparisons.StructuralEqualityComparer);
            return Equals(x, y);
        };

        public Func<object, PropertyInfo, PropertyInfo> GetSamePropertyStrategy { get; set; } = (obj, prop) =>
        {
            var res = obj.GetType().GetProperty(prop.Name);
            Guards.InternalErrorIfNull(res, $"property {prop.Name} not found in object of type {obj.GetType()}");
            return res;
        };

        // public Action<

        // compare current and new results, apply new results to current and return changes
        public IEnumerable<Change> DiffAndPatch<TCurrent, TNew>(TCurrent? current, TNew changed)
            where TCurrent : class
            where TNew : class
        {
            if (current == null)
            {
                // in this case we fully replace all content of object - use https://json8.github.io/patch/demos/apply/ to test
                yield return new Change
                {
                    op = Operations.replace,
                    path = "",
                    value = changed
                };
                yield break;
            }
            return DiffAndPatch(current, changed, "");
        }

        #region DiffAndPatch

        private IEnumerable<Change> DiffAndPatch(object current, object changed, string parentPath)
        {
            var props = changed.GetType().GetProperties();
            foreach (var prop in props)
            {
                if (SkipStrategy != null && SkipStrategy(prop))
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

        private IEnumerable<Change> DiffAndPatchValue(object current, PropertyInfo prop, object changed, string childFieldName)
        {
            var newValue = prop.GetValue(changed);
            var currentProp = GetSamePropertyStrategy(current, prop);
            var oldValue = currentProp.GetValue(current);

            if (!AreEqualsStrategy(oldValue, newValue))
            {
                if (SetValueStrategy(currentProp, current, newValue))
                {
                    yield return new Change
                    {
                        path = childFieldName,
                        op = Operations.replace,
                        value = newValue
                    };
                }
            }
        }



        private IEnumerable<Change> DiffAndPatchContainer(object current, PropertyInfo prop, object changed, string currentpatch)
        {
            var currentProp = GetSamePropertyStrategy(current, prop);
            var currentChild = currentProp.GetValue(current);
            var changedChild = prop.GetValue(changed);

            if (currentChild != null && changedChild == null)
            {
                prop.SetValue(current, null);
                // remove old view
                yield return new Change
                {
                    op = Operations.remove,
                    path = currentpatch
                };
            }
            else if (currentChild == null && changedChild != null)
            {
                //add new view
                currentChild = Activator.CreateInstance(prop.PropertyType);
                Guards.InternalErrorIfNull(currentChild, $"failed to create new item of type {prop.PropertyType}");
                prop.SetValue(current, currentChild);
                var skipped = DiffAndPatch(currentChild, changedChild, currentpatch).ToArray();

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
            else
            {
                // do nothing if both view and model are null
            }
        }

        class OldNewPair
        {
            public OldNewPair(object m)
            {
                Newitem = m;
            }

            internal object Newitem { get; set; }
            internal object? CurrentItem { get; set; }
        }

        private IEnumerable<Change> DiffAndPatchDictionary(
            object current, PropertyInfo prop, object changed, string currentpatch)
        {
            var propInCurrent = GetSamePropertyStrategy(current, prop);

            Guards.InternalErrorIfFalse(propInCurrent.IsAssociativeDictionary(),
               $"Field {propInCurrent.Name} should be dictionary");

            var currentDict = propInCurrent.GetValue(current) as IDictionary<string, object>;
            var changedDict = propInCurrent.GetValue(changed) as IDictionary<string, object>;

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
                        if (!AreEqualsStrategy(curItem, changedItem.Value))
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
            var propInCurrent = GetSamePropertyStrategy(current, prop);
            var rawCurrentColl = propInCurrent.GetValue(current);
            Guards.InternalErrorIfFalse(rawCurrentColl is IList,
                $"Collection {propInCurrent.Name} should implement IList interface");

            var currentColl = (IList)rawCurrentColl;

            var itemType = ReflectionEx.GetItemType(currentColl);

            var rawChangedColl = prop.GetValue(changed);

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
                    GetUniqueIdStrategy,
                    (x) =>
                    {
                        var newObj = CreateStrategy(x.GetType());
                        SetUniqueIdStrategy(newObj, x);
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
                var old = oldNewPairs.FirstOrDefault(mv => mv.Newitem != null && getUniquieId(mv.Newitem) == oldId);
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

                    pair.CurrentItem = createFrom(pair.Newitem);
                    oldCollection.Insert(pos, pair.CurrentItem);

                    var curpatch = $"{currentpatch}/{pos}";

                    // update current from changed without registry changes in result (it is why new List<Change> is used)
                    // this initialized view will be set into value
                    if (!pair.CurrentItem.GetType().IsSimpleTypeOrString())
                    {
                        var sipped = DiffAndPatch(pair.CurrentItem, pair.Newitem, curpatch).ToArray();
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
                    foreach (var c in MoveAndUpdate(p-1))
                        yield return c;
            }
            // move and update

            IEnumerable<Change> MoveAndUpdate(int pos)
            {
                var pair = oldNewPairs[pos];
                Guards.InternalErrorIfNull(pair.Newitem, $"All views should already created and bound with model");
                Guards.InternalErrorIfNull(pair.CurrentItem, "All views should already created and bound with model");

                var oldPos = oldCollection.IndexOf(pair.CurrentItem);

                var curpatch = $"{currentpatch}/{pos}";
                if (oldPos != pos)
                {
                    var fromPath = $"{currentpatch}/{oldPos}";
                    oldCollection.RemoveAt(oldPos);
                    oldCollection.Insert(pos, pair.CurrentItem);

                    yield return  new Change
                    {
                        op = Operations.move,
                        path = curpatch,
                        from = fromPath
                    };
                }

                if (!pair.CurrentItem.GetType().IsSimpleTypeOrString())
                {
                    foreach (var c in DiffAndPatch(pair.CurrentItem, pair.Newitem, curpatch))
                        yield return c;
                }
            }
        }

        #endregion DiffAndPatch
    }
}