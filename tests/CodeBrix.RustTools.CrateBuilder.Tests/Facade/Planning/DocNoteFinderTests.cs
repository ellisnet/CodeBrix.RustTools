using CodeBrix.RustTools.CrateBuilder.Facade.Planning;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Planning;

public class DocNoteFinderTests
{
    [Fact]
    public void Find_returns_the_sentences_that_state_units_or_rules()
    {
        //Arrange
        const string docs = "Sets the delay. The delay is in milliseconds. Colors are premultiplied.\n\nMust be called from the main thread.";

        //Act
        var notes = DocNoteFinder.Find(docs);

        //Assert
        notes.Should().Equal("The delay is in milliseconds.", "Colors are premultiplied.", "Must be called from the main thread.");
    }

    [Fact]
    public void Find_ignores_fenced_code_and_plain_sentences()
    {
        //Arrange
        const string docs = "Creates a vector.\n\n```\nlet x = per_second(); // must\n```";

        //Act
        var notes = DocNoteFinder.Find(docs);

        //Assert
        notes.Should().BeEmpty();
    }

    [Fact]
    public void Find_of_empty_docs_is_empty() => DocNoteFinder.Find(string.Empty).Should().BeEmpty();
}
