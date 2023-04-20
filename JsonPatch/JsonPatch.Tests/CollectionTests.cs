using Def.JsonPatch;

#pragma warning disable NUnit2005 // Consider using Assert.That(actual, Is.EqualTo(expected)) instead of Assert.AreEqual(expected, actual)

namespace JsonPatch.Tests
{
    public class CollectionTests
    {
        public class Item
        {
            public string? UniqueId { get; set; }
            public string? Name { get; set; }
        }
        public class Main
        {
            public int Id { get; set; }
            public List<Item>? Items { get; set; }
        }

        Differ differ;

        Main input;

        [SetUp]
        public void Setup()
        {
            input = new Main
            {
                Id = 1,
                Items = new List<Item> {
                    new Item {
                        Name = "Item1",
                        UniqueId = "id1",
                    },
                    new Item {
                        Name = "Item2",
                        UniqueId = "id2"
                    },
                }
            };

            differ = new Differ(new DifferStrategies
            {
                AreSame = (x, y) =>
                {
                    if (x is Item xv && y is Item yv)
                        return xv.UniqueId == yv.UniqueId;
                    return false;
                },
            });
        }

        [Test]
        public void UpdateItem()
        {
            var modified = new
            {
                Items = new[]
                {
                    new Item
                    {
                        Name = "updated1",
                        UniqueId = "id1",
                    },
                    new Item {
                        Name = "Item2",
                        UniqueId = "id2"
                    },                  
                }
            };
            var changes = differ.DiffAndPatch(input, modified).ToArray();

            Assert.AreEqual("updated1", input.Items[0].Name);
            Assert.AreEqual("Item2", input.Items[1].Name);

            Assert.AreEqual(1, changes.Length);
            Assert.AreEqual("updated1", changes[0].value);
        }

        [Test]
        public void AddItem()
        {
            var modified = new
            {
                Items = new[]
                {
                    new Item
                    {
                        Name = "added",
                        UniqueId = "id3",
                    },
                    new Item
                    {
                        Name = "Item1",
                        UniqueId = "id1",
                    },
                    new Item {
                        Name = "Item2",
                        UniqueId = "id2"
                    },
                }
            };
            var changes = differ.DiffAndPatch(input, modified).ToArray();

            Assert.AreEqual("added", input.Items[0].Name);

            Assert.AreEqual(1, changes.Length);
            Assert.AreEqual(Operations.add, changes[0].op);
        }
        [Test]
        public void RemoveItem()
        {
            var modified = new
            {
                Items = new[]
                {
                    new Item {
                        Name = "Item2",
                        UniqueId = "id2"
                    },
                }
            };
            var changes = differ.DiffAndPatch(input, modified).ToArray();

            Assert.AreEqual(1, input.Items.Count);

            Assert.AreEqual(1, changes.Length);
            Assert.AreEqual(Operations.remove, changes[0].op);
        }

        [Test]
        public void AddDuplicatedItem()
        {
            var modified = new
            {
                Items = new[]
                {
                    new Item
                    {
                        Name = "Item1",
                        UniqueId = "id1",
                    },
                    new Item
                    {
                        Name = "Item1",
                        UniqueId = "id1",
                    },
                    new Item {
                        Name = "Item2",
                        UniqueId = "id2"
                    },
                }
            };
            var changes = differ.DiffAndPatch(input, modified).ToArray();

            Assert.AreEqual("Item1", input.Items[0].Name);

            Assert.AreEqual(1, changes.Length);
            Assert.AreEqual(Operations.add, changes[0].op);
        }
        [Test]
        public void UpdateDuplicatedItem()
        {
            input.Items.Add(new Item
            {
                Name = "Item2",
                UniqueId = "id2"
            });

            var modified = new
            {
                Items = new[]
                {
                    new Item
                    {
                        Name = "Item1",
                        UniqueId = "id1",
                    },
                    new Item
                    {
                        Name = "Item1",
                        UniqueId = "id1",
                    },
                    new Item {
                        Name = "Item2",
                        UniqueId = "id2"
                    },
                }
            };
            var ex = Assert.Throws< InvalidOperationException>(() =>
                differ.DiffAndPatch(input, modified).ToArray());
            Assert.AreEqual("Object in old collection for type JsonPatch.Tests.CollectionTests+Item exist more than once.", ex.Message);
        }
    }
}