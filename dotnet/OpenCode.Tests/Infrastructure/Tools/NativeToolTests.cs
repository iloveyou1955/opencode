using System.ComponentModel;
using OpenCode.Core.Attributes;
using OpenCode.Core.Contracts;
using OpenCode.Infrastructure.Tools;

namespace OpenCode.Tests.Infrastructure.Tools;

public class CalculatorService
{
    [Tool("add", "Add two numbers")]
    public int Add(
        [Description("The first number")] int a, 
        [Description("The second number")] int b)
    {
        return a + b;
    }

    [Tool("greet", "Greet a user")]
    public string Greet(
        [Description("The name of the user")] string name, 
        [Description("Is it morning?")] bool isMorning = true)
    {
        return isMorning ? $"Good morning, {name}!" : $"Hello, {name}!";
    }
}

public class NativeToolTests
{
    [Fact]
    public async Task NativeTool_ShouldExecuteMethod()
    {
        // Arrange
        var service = new CalculatorService();
        var context = new MockToolContext();
        var tools = ToolFactory.CreateTools(service).ToList();
        
        var addTool = tools.First(t => t.Name == "add");
        
        var args = new System.Text.Json.Nodes.JsonObject
        {
            ["a"] = 5,
            ["b"] = 3
        };

        // Act
        var result = await addTool.ExecuteAsync(args, context);

        // Assert
        Assert.Equal("8", result);
    }

    [Fact]
    public async Task NativeTool_ShouldHandleDefaultValues()
    {
        // Arrange
        var service = new CalculatorService();
        var context = new MockToolContext();
        var tools = ToolFactory.CreateTools(service).ToList();
        
        var greetTool = tools.First(t => t.Name == "greet");
        
        var args = new System.Text.Json.Nodes.JsonObject
        {
            ["name"] = "Alice"
        };

        // Act
        var result = await greetTool.ExecuteAsync(args, context);

        // Assert
        Assert.Equal("Good morning, Alice!", result);
    }

    [Fact]
    public void NativeTool_ShouldGenerateSchema()
    {
        // Arrange
        var service = new CalculatorService();
        var tools = ToolFactory.CreateTools(service).ToList();
        var addTool = tools.First(t => t.Name == "add");

        // Act
        var schema = addTool.InputSchema;

        // Assert
        Assert.Contains("a", schema);
        Assert.Contains("b", schema);
        Assert.Contains("The first number", schema);
        Assert.Contains("integer", schema);
    }
}
