using Def.JsonPatch;

#pragma warning disable NUnit2005 // Consider using Assert.That(actual, Is.EqualTo(expected)) instead of Assert.AreEqual(expected, actual)

namespace JsonPatch.Tests
{
    public class IntCollectionTests
    {
        public class Main
        {
            public int Id { get; set; }
            public List<int>? Items { get; set; }
        }

        Differ differ;

        [SetUp]
        public void Setup()
        {
            differ = new Differ(new DifferStrategies());
        }

        [TestCase("1;2", "1;3", "remove;add")] 
        [TestCase("1;2", "1", "remove")] //
        [TestCase("1;2", "1;2;3", "add")]
        [TestCase("1;2", "4;5", "remove;remove;add;add")]
        [TestCase("", "1;2", "add;add")]
        [TestCase("1;2", "", "remove;remove")]
        public void UpdateItem(string input, string changed, string expectedOperations)
        {
            var initial = new Main
            {
                Id = 1,
                Items = input.Split(";", StringSplitOptions.RemoveEmptyEntries).Select(x => int.Parse(x)).ToList()
            };
            var modified = new
            {
                Items = changed.Split(";", StringSplitOptions.RemoveEmptyEntries).Select(x => int.Parse(x)).ToList()
            };
            var changes = differ.DiffAndPatch(initial, modified).ToArray();

            var res = string.Join(';', initial.Items.Select(x => x.ToString()));
            Assert.AreEqual(changed, res);

            var operations = string.Join(';',changes.Select(c => c.op.ToString()));
            Assert.AreEqual(expectedOperations, operations);
        }

    }
}
