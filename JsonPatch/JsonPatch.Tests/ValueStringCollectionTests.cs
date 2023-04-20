using Def.JsonPatch;

#pragma warning disable NUnit2005 // Consider using Assert.That(actual, Is.EqualTo(expected)) instead of Assert.AreEqual(expected, actual)

namespace JsonPatch.Tests
{
    public class ValueStringCollectionTests
    {
        public class Main
        {
            public int Id { get; set; }
            public List<string>? Items { get; set; }
        }

        Differ differ;

        [SetUp]
        public void Setup()
        {
            differ = new Differ(new DifferStrategies());
        }

        [TestCase("v1;v2", "v1;upd", "remove;add")]
        [TestCase("v1;v2", "v1", "remove")]
        [TestCase("v1;v2", "v1;v2;v3", "add")]
        [TestCase("v1;v2", "v4;v5", "remove;remove;add;add")]
        [TestCase("", "v1;v2", "add;add")]
        [TestCase("v1;v2", "", "remove;remove")]
        public void UpdateItem(string input, string changed, string expectedOperations)
        {
            var initial = new Main
            {
                Id = 1,
                Items = input.Split(";", StringSplitOptions.RemoveEmptyEntries).ToList()
            };
            var modified = new
            {
                Items = changed.Split(";", StringSplitOptions.RemoveEmptyEntries).ToList()
            };
            var changes = differ.DiffAndPatch(initial, modified).ToArray();

            var res = string.Join(';', initial.Items);
            Assert.AreEqual(changed, res);

            var operations = string.Join(';', changes.Select(c => c.op.ToString()));
            Assert.AreEqual(expectedOperations, operations);

        }

    }
}