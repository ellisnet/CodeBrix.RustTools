using System;
using System.Collections.Generic;
using System.Text;
using CodeBrix.RustTools.Docker.Internal;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Containers;

public class LineSplitterTests
{
    [Fact]
    public void Append_reports_complete_lines_without_terminators()
    {
        //Arrange
        var lines = new List<string>();
        var splitter = new LineSplitter(lines.Add);

        //Act
        splitter.Append(Encoding.UTF8.GetBytes("one\r\ntwo\nthr"));
        splitter.Append(Encoding.UTF8.GetBytes("ee\n"));
        splitter.Complete();

        //Assert
        lines.Should().Equal("one", "two", "three");
        splitter.Text.Should().Be("one\r\ntwo\nthree\n");
    }

    [Fact]
    public void Complete_reports_a_final_line_without_a_terminator()
    {
        //Arrange
        var lines = new List<string>();
        var splitter = new LineSplitter(lines.Add);

        //Act
        splitter.Append(Encoding.UTF8.GetBytes("last"));
        splitter.Complete();

        //Assert
        lines.Should().Equal("last");
    }

    [Fact]
    public void Append_handles_a_character_split_across_chunks()
    {
        //Arrange
        var lines = new List<string>();
        var splitter = new LineSplitter(lines.Add);
        var bytes = Encoding.UTF8.GetBytes("Nöldeke\n");

        //Act
        splitter.Append(bytes.AsSpan(0, 2));
        splitter.Append(bytes.AsSpan(2));
        splitter.Complete();

        //Assert
        lines.Should().Equal("Nöldeke");
    }

    [Fact]
    public void Append_works_without_a_callback()
    {
        //Arrange
        var splitter = new LineSplitter(null);

        //Act
        splitter.Append(Encoding.UTF8.GetBytes("a\nb"));
        splitter.Complete();

        //Assert
        splitter.Text.Should().Be("a\nb");
    }
}
