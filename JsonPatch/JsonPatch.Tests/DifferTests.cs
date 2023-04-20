using Def.JsonPatch;

namespace JsonPatch.Tests
{

    public class DifferTests
    {
        public class View
        {
            public string? UniqueId { get; set; }
            public string? Name { get; set; }
        }
        Differ differ;

        [SetUp]
        public void Setup()
        {
            differ = new Differ(new DifferStrategies
            {
                Skip = (propertyInfo) => propertyInfo.Name == nameof(View.UniqueId),
                AreSame = (x, y) =>
                {
                    if (x is View xv && y is View yv)
                        return xv.UniqueId == yv.UniqueId;
                    return false;
                },
                SetUniqueId = (from, to) =>
                {
                    var n = (View)from;
                    n.UniqueId = (from as View)?.UniqueId;
                }
            });
        }

        [Test]
        public void Test1()
        {
            var v1 = new View
            {
                UniqueId = "x1",
                Name = "Name",
            };

            var changes = differ.DiffAndPatch(v1, new { Name = "Name2" }).ToArray();

            Assert.AreEqual("Name2", v1.Name);
        }

        [Test]
        public void ExpectedNull()
        {
            var v1 = new
            {
                UniqueId = "x1",
                Name = "Name",
            };

            var ex = Assert.Throws<ArgumentException>(() =>
                differ.DiffAndPatch(v1, new { Name = "Name2" }).ToArray());

            Assert.AreEqual("Property set method not found.", ex.Message);
        }
    }
}