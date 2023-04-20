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
           differ = new Differ
            {
                SkipStrategy = (propertyInfo) => propertyInfo.Name == nameof(View.UniqueId),
                GetUniqueIdStrategy = (x) =>
                {
                    if (x is View v)
                        return v.UniqueId ?? throw new ArgumentException("UniqueId should be not null");
                    throw new ArgumentException($"Unknown object {x.GetType()}");
                },
                SetUniqueIdStrategy = (from, to) =>
                {
                    var n = (View)from;
                    n.UniqueId = (from as View)?.UniqueId;
                }
            };        
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