using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Abp.Dependency;

namespace ToeicExam.Web.Host.Services
{
    public class ToeicWordExamParser : ITransientDependency
    {
        private static readonly Regex PartRegex = new Regex(
            @"<PART\b(?<attributes>[^>]*)>(?<body>.*?)</PART>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public ImportedExam Parse(Stream stream)
        {
            string source;
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, true))
            {
                var document = archive.GetEntry("word/document.xml");
                if (document == null)
                {
                    throw new InvalidDataException("File Word không có word/document.xml.");
                }

                using (var input = document.Open())
                {
                    var xml = new XmlDocument();
                    xml.XmlResolver = null;
                    xml.Load(input);
                    var namespaces = new XmlNamespaceManager(xml.NameTable);
                    namespaces.AddNamespace("w", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
                    var text = new StringBuilder();
                    foreach (XmlNode paragraph in xml.SelectNodes("//w:p", namespaces))
                    {
                        foreach (XmlNode run in paragraph.SelectNodes(".//w:t", namespaces))
                        {
                            text.Append(run.InnerText);
                        }

                        text.AppendLine();
                    }

                    source = text.ToString();
                }
            }

            var examMatch = Regex.Matches(source, @"<EXAM\b(?<attributes>[^>]*)>", RegexOptions.IgnoreCase)
                .Cast<Match>()
                .FirstOrDefault(match => Attributes(match.Groups["attributes"].Value).ContainsKey("title"));
            if (!examMatch.Success)
            {
                throw new InvalidDataException("Không tìm thấy thẻ <EXAM> trong file Word theo mẫu TOEIC.");
            }

            var examAttributes = Attributes(examMatch.Groups["attributes"].Value);
            var expectedQuestionCount = IntegerAttribute(examAttributes, "question_count", 0);
            var exam = new ImportedExam
            {
                Title = examAttributes.ContainsKey("title") ? examAttributes["title"] : Path.GetFileNameWithoutExtension("TOEIC"),
                DurationMinutes = IntegerAttribute(examAttributes, "duration_minutes", 75),
                Parts = new List<ImportedPart>()
            };

            foreach (Match partMatch in PartRegex.Matches(source.Substring(examMatch.Index)))
            {
                var attributes = Attributes(partMatch.Groups["attributes"].Value);
                var body = partMatch.Groups["body"].Value;
                int number = IntegerAttribute(attributes, "id", 0);
                if (number < 5 || number > 7)
                {
                    continue;
                }

                var part = new ImportedPart
                {
                    Number = number,
                    Title = "Part " + number,
                    Mode = attributes.ContainsKey("type") ? attributes["type"] : "standalone",
                    Directions = TagText(body, "DIRECTIONS"),
                    Passages = new List<ImportedPassage>(),
                    Questions = new List<ImportedQuestion>()
                };

                var passageMatches = Regex.Matches(
                    body,
                    @"<PASSAGE\b(?<attributes>[^>]*)>(?<body>.*?)</PASSAGE>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);
                foreach (Match passageMatch in passageMatches)
                {
                    var passageAttributes = Attributes(passageMatch.Groups["attributes"].Value);
                    var passageBody = passageMatch.Groups["body"].Value;
                    var passageId = passageAttributes.ContainsKey("id") ? passageAttributes["id"] : "";
                    part.Passages.Add(new ImportedPassage
                    {
                        ExternalId = passageId,
                        Type = passageAttributes.ContainsKey("type") ? passageAttributes["type"] : null,
                        Content = FormatPassage(TagText(passageBody, "PASSAGE_TEXT"))
                    });
                }

                foreach (Match questionMatch in Regex.Matches(
                    body,
                    @"<QUESTION\b(?<attributes>[^>]*)>(?<body>.*?)</QUESTION>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase))
                {
                    var questionAttributes = Attributes(questionMatch.Groups["attributes"].Value);
                    var questionBody = questionMatch.Groups["body"].Value;
                    var questionNumber = IntegerAttribute(questionAttributes, "id", 0);
                    if (questionNumber < 1)
                    {
                        continue;
                    }

                    var answerMatches = Regex.Matches(
                        questionBody,
                        @"<ANSWER\b[^>]*>(?<label>.*?)</ANSWER>",
                        RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    if (answerMatches.Count > 1)
                    {
                        throw new InvalidDataException("Mỗi câu hỏi chỉ được có một thẻ <ANSWER>.");
                    }

                    string correctLabel = null;
                    if (answerMatches.Count == 1)
                    {
                        var wrappedOptionMatch = Regex.Match(
                            answerMatches[0].Groups["label"].Value,
                            @"<OPTION\b(?<attributes>[^>]*)>",
                            RegexOptions.IgnoreCase);
                        if (wrappedOptionMatch.Success)
                        {
                            var answerOptionAttributes = Attributes(wrappedOptionMatch.Groups["attributes"].Value);
                            if (!answerOptionAttributes.ContainsKey("label"))
                            {
                                throw new InvalidDataException(
                                    "Thẻ <OPTION> bên trong <ANSWER> cần có thuộc tính label.");
                            }

                            correctLabel = answerOptionAttributes["label"].Trim().ToUpperInvariant();
                        }
                        else
                        {
                            correctLabel = Clean(answerMatches[0].Groups["label"].Value).ToUpperInvariant();
                        }
                    }
                    var options = new List<ImportedOption>();
                    foreach (Match optionMatch in Regex.Matches(
                        questionBody,
                        @"<OPTION\b(?<attributes>[^>]*)>(?<body>.*?)</OPTION>",
                        RegexOptions.Singleline | RegexOptions.IgnoreCase))
                    {
                        var optionAttributes = Attributes(optionMatch.Groups["attributes"].Value);
                        if (optionAttributes.ContainsKey("label"))
                        {
                            options.Add(new ImportedOption
                            {
                                Label = optionAttributes["label"],
                                Content = Clean(optionMatch.Groups["body"].Value)
                            });
                        }
                    }

                    if (correctLabel != null &&
                        (!new[] { "A", "B", "C", "D" }.Contains(correctLabel) ||
                         !options.Any(option => string.Equals(option.Label, correctLabel, StringComparison.OrdinalIgnoreCase))))
                    {
                        throw new InvalidDataException(
                            "Đáp án trong thẻ <ANSWER> phải là A, B, C hoặc D và phải khớp với lựa chọn của câu " +
                            questionNumber + ".");
                    }

                    part.Questions.Add(new ImportedQuestion
                    {
                        Number = questionNumber,
                        PassageExternalId = questionAttributes.ContainsKey("passage_id") ? questionAttributes["passage_id"] : null,
                        Content = QuestionText(questionBody),
                        Options = options,
                        CorrectLabel = correctLabel
                    });
                }

                part.Questions = part.Questions.OrderBy(question => question.Number).ToList();
                part.Passages = part.Passages.OrderBy(passage => FirstNumber(passage.ExternalId)).ToList();
                exam.Parts.Add(part);
            }

            exam.Parts = exam.Parts.OrderBy(part => part.Number).ToList();
            if (exam.Parts.Count == 0 || exam.Parts.Sum(part => part.Questions.Count) == 0)
            {
                throw new InvalidDataException("Không tìm thấy câu hỏi trong file Word.");
            }

            if (expectedQuestionCount > 0 && exam.Parts.Sum(part => part.Questions.Count) != expectedQuestionCount)
            {
                throw new InvalidDataException(
                    "Số câu trong nội dung không khớp với question_count của thẻ EXAM (" +
                    expectedQuestionCount + ").");
            }

            return exam;
        }

        private static string TagText(string source, string tag)
        {
            var match = Regex.Match(source, "<" + tag + @"\b[^>]*>(?<text>.*?)</" + tag + ">",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["text"].Value : "";
        }

        private static string QuestionText(string questionBody)
        {
            var match = Regex.Match(
                questionBody,
                @"<STEM\b[^>]*>(?<text>.*?)(?:</STEM>|(?=<OPTION\b))",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return match.Success ? Clean(match.Groups["text"].Value) : "";
        }

        private static string FormatPassage(string value)
        {
            value = Regex.Replace(value, @"<BLANK\b(?<attributes>[^>]*)/\s*>", match =>
            {
                var attributes = Attributes(match.Groups["attributes"].Value);
                var number = attributes.ContainsKey("id") ? attributes["id"] : "";
                return "________ (" + number + ")";
            }, RegexOptions.IgnoreCase);
            return Clean(value).Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static string Clean(string value)
        {
            return Regex.Replace(value, @"\s+", " ").Trim();
        }

        private static Dictionary<string, string> Attributes(string value)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(value, @"(?<name>[\w-]+)\s*=\s*""(?<value>[^""]*)"""))
            {
                result[match.Groups["name"].Value] = match.Groups["value"].Value;
            }

            return result;
        }

        private static int IntegerAttribute(Dictionary<string, string> attributes, string name, int fallback)
        {
            int value;
            return attributes.ContainsKey(name) && int.TryParse(attributes[name], out value) ? value : fallback;
        }

        private static int FirstNumber(string value)
        {
            var match = Regex.Match(value ?? "", @"\d+");
            int number;
            return match.Success && int.TryParse(match.Value, out number) ? number : 0;
        }
    }

    public class ImportedExam
    {
        public string Title { get; set; }
        public int DurationMinutes { get; set; }
        public List<ImportedPart> Parts { get; set; }
    }

    public class ImportedPart
    {
        public int Number { get; set; }
        public string Title { get; set; }
        public string Mode { get; set; }
        public string Directions { get; set; }
        public List<ImportedPassage> Passages { get; set; }
        public List<ImportedQuestion> Questions { get; set; }
    }

    public class ImportedPassage
    {
        public string ExternalId { get; set; }
        public string Type { get; set; }
        public string Content { get; set; }
    }

    public class ImportedQuestion
    {
        public int Number { get; set; }
        public string PassageExternalId { get; set; }
        public string Content { get; set; }
        public List<ImportedOption> Options { get; set; }
        public string CorrectLabel { get; set; }
    }

    public class ImportedOption
    {
        public string Label { get; set; }
        public string Content { get; set; }
    }
}
