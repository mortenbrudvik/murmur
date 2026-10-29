using Murmur.Common;
using Shouldly;

namespace Murmur.Core.Tests.Common;

public class JsonUtilsTests
{
    private record TestObject(string FirstName, int Age);

    private enum TestMode { FastMode, Slow }

    private record TestModeObject(TestMode Mode);

    [Fact]
    public void Serialize_UsesCamelCase()
    {
        var obj = new TestObject("Alice", 30);

        var json = JsonUtils.Serialize(obj);

        json.ShouldContain("\"firstName\"");
        json.ShouldContain("\"age\"");
        // Verify PascalCase keys are NOT present (case-sensitive check)
        json.Contains("\"FirstName\"", StringComparison.Ordinal).ShouldBeFalse();
        json.Contains("\"Age\"", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void Deserialize_ParsesCaseInsensitive()
    {
        var json = """{"FirstName":"Bob","age":25}""";

        var result = JsonUtils.Deserialize(json, typeof(TestObject)) as TestObject;

        result.ShouldNotBeNull();
        result.FirstName.ShouldBe("Bob");
        result.Age.ShouldBe(25);
    }

    [Fact]
    public void Serialize_WritesEnumsAsCamelCaseStrings()
    {
        var json = JsonUtils.Serialize(new TestModeObject(TestMode.FastMode));

        json.ShouldBe("""{"mode":"fastMode"}""");
    }

    [Fact]
    public void Deserialize_ReadsEnumsFromCamelCaseStrings()
    {
        var json = """{"mode":"fastMode"}""";

        var result = JsonUtils.Deserialize(json, typeof(TestModeObject)) as TestModeObject;

        result.ShouldNotBeNull();
        result.Mode.ShouldBe(TestMode.FastMode);
    }
}
