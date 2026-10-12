using Shouldly;
using System;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Text;
using System.Security;
using ToeicExam.Web.Host.Services;
using Xunit;

namespace ToeicExam.Web.Tests.Services;

public class ToeicWordExamParser_Tests
{
    [Fact]
    public void Parses_tagged_reading_test_10_without_inventing_answers()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "TOEIC_Reading_Test_10_Tagged.docx");
        var parser = new ToeicWordExamParser();

        using var stream = File.OpenRead(filePath);
        var exam = parser.Parse(stream);

        exam.Title.ShouldBe("TOEIC Reading Test 10");
        exam.DurationMinutes.ShouldBe(75);
        exam.Parts.Select(part => part.Number).ShouldBe(new[] { 5, 6, 7 });
        exam.Parts.Sum(part => part.Questions.Count).ShouldBe(100);
        exam.Parts.SelectMany(part => part.Questions).ShouldAllBe(question => question.Options.Count == 4);
        exam.Parts.SelectMany(part => part.Passages).ShouldNotBeEmpty();
    }

    [Fact]
    public void Parses_all_answers_from_answer_tagged_reading_test_10()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "TOEIC_Reading_Test_10_with_ANSWER_tags.docx");
        var parser = new ToeicWordExamParser();

        using var stream = File.OpenRead(filePath);
        var exam = parser.Parse(stream);
        var questions = exam.Parts.SelectMany(part => part.Questions).ToList();

        questions.Count.ShouldBe(100);
        questions.ShouldAllBe(question =>
            !string.IsNullOrWhiteSpace(question.Content) &&
            question.CorrectLabel != null &&
            question.Options.Any(option => option.Label == question.CorrectLabel));
        questions[0].Content.ShouldContain("Please contact the product distributor");
    }

    [Fact]
    public void Parses_answer_tag_as_correct_option()
    {
        var exam = ParseTaggedQuestion("<ANSWER>C</ANSWER>");

        exam.Parts.Single().Questions.Single().CorrectLabel.ShouldBe("C");
    }

    [Fact]
    public void Parses_answer_tag_wrapping_correct_option()
    {
        var exam = ParseTaggedQuestion(
            "<ANSWER><OPTION label=\"C\">Third</OPTION></ANSWER>",
            "<OPTION label=\"A\">First</OPTION><OPTION label=\"B\">Second</OPTION>" +
            "<OPTION label=\"D\">Fourth</OPTION>");

        exam.Parts.Single().Questions.Single().CorrectLabel.ShouldBe("C");
    }

    [Fact]
    public void Parses_question_text_when_stem_closing_tag_is_missing()
    {
        var exam = ParseTaggedQuestion(
            "<ANSWER>C</ANSWER>",
            "<OPTION label=\"A\">First</OPTION><OPTION label=\"B\">Second</OPTION>" +
            "<OPTION label=\"C\">Third</OPTION>" +
            "<OPTION label=\"D\">Fourth</OPTION>",
            stem: "Question text without closing tag",
            closeStem: false);

        exam.Parts.Single().Questions.Single().Content.ShouldBe("Question text without closing tag");
    }

    [Theory]
    [InlineData("<ANSWER>E</ANSWER>")]
    [InlineData("<ANSWER>A</ANSWER><ANSWER>B</ANSWER>")]
    public void Rejects_invalid_answer_tags(string answerTag)
    {
        Should.Throw<InvalidDataException>(() => ParseTaggedQuestion(answerTag));
    }

    private static ImportedExam ParseTaggedQuestion(
        string answerTag,
        string options = null,
        string stem = "Question?",
        bool closeStem = true)
    {
        options = options ??
            "<OPTION label=\"A\">First</OPTION><OPTION label=\"B\">Second</OPTION>" +
            "<OPTION label=\"C\">Third</OPTION><OPTION label=\"D\">Fourth</OPTION>";
        var taggedContent =
            "<EXAM title=\"Test\" duration_minutes=\"1\" question_count=\"1\">" +
            "<PART id=\"5\"><QUESTION id=\"1\"><STEM>" + stem +
            (closeStem ? "</STEM>" : "") +
            options +
            answerTag + "</QUESTION></PART></EXAM>";
        var documentXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:body><w:p><w:r><w:t>" + SecurityElement.Escape(taggedContent) +
            "</w:t></w:r></w:p></w:body></w:document>";
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(archive.CreateEntry("word/document.xml").Open(), Encoding.UTF8))
        {
            writer.Write(documentXml);
        }

        stream.Position = 0;
        return new ToeicWordExamParser().Parse(stream);
    }
}
