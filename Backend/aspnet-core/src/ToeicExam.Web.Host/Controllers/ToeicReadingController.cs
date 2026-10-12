using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Identity;
using Abp.Dependency;
using System;
using System.Collections.Generic;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ToeicExam.Web.Host.Services;

namespace ToeicExam.Web.Host.Controllers
{
    [ApiController]
    [IgnoreAntiforgeryToken]
    [Route("api")]
    public class ToeicReadingController : ControllerBase, ITransientDependency
    {
        private readonly IConfiguration _configuration;
        private readonly ToeicWordExamParser _parser;

        public ToeicReadingController(IConfiguration configuration, ToeicWordExamParser parser)
        {
            _configuration = configuration;
            _parser = parser;
        }

        [AllowAnonymous]
        [HttpPost("auth/login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest input)
        {
            if (input == null || string.IsNullOrWhiteSpace(input.Email) || string.IsNullOrEmpty(input.Password))
            {
                return BadRequest(new { message = "Nhập email và mật khẩu." });
            }

            using (var connection = await OpenConnectionAsync())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT user_id, full_name, email, password_hash, role, is_active
FROM dbo.users WHERE email = @email AND role = 'admin'";
                command.Parameters.AddWithValue("@email", input.Email.Trim());
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (!await reader.ReadAsync() || !reader.GetBoolean(5))
                    {
                        return Unauthorized(new { message = "Email hoặc mật khẩu không chính xác." });
                    }

                    var userId = reader.GetInt64(0);
                    var fullName = reader.GetString(1);
                    var email = reader.GetString(2);
                    var passwordHash = reader.GetString(3);
                    var role = reader.GetString(4);
                    var verification = new PasswordHasher<string>().VerifyHashedPassword(email, passwordHash, input.Password);
                    if (verification == PasswordVerificationResult.Failed)
                    {
                        return Unauthorized(new { message = "Email hoặc mật khẩu không chính xác." });
                    }

                    var token = CreateToken(userId, fullName, email, role);
                    return Ok(new
                    {
                        accessToken = token,
                        user = new { userId = userId, fullName = fullName, email = email, role = role }
                    });
                }
            }
        }

        [Authorize]
        [HttpGet("auth/me")]
        public IActionResult CurrentUser()
        {
            return Ok(new
            {
                userId = CurrentUserId,
                fullName = User.FindFirst(ClaimTypes.Name)?.Value,
                email = User.FindFirst(ClaimTypes.Email)?.Value,
                role = IsAdmin ? "admin" : "user"
            });
        }

        [AllowAnonymous]
        [HttpGet("exams")]
        public async Task<IActionResult> PublishedExams()
        {
            using (var connection = await OpenConnectionAsync())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT e.exam_id, e.title, e.description, e.duration_minutes, e.status,
    e.created_at, COUNT(q.question_id) AS question_count,
    (SELECT STRING_AGG(CONCAT('Part ', p.part_number), ' • ') WITHIN GROUP (ORDER BY p.order_no)
     FROM dbo.exam_parts p WHERE p.exam_id = e.exam_id) AS parts
FROM dbo.exams e
LEFT JOIN dbo.exam_parts ep ON ep.exam_id = e.exam_id
LEFT JOIN dbo.questions q ON q.part_id = ep.part_id
WHERE e.status = 'published'
GROUP BY e.exam_id, e.title, e.description, e.duration_minutes, e.status, e.created_at
ORDER BY e.created_at DESC";
                return Ok(await ReadDictionariesAsync(command));
            }
        }

        [Authorize(Roles = "admin")]
        [HttpGet("admin/exams")]
        public async Task<IActionResult> ManageExams()
        {
            using (var connection = await OpenConnectionAsync())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT e.exam_id, e.title, e.description, e.duration_minutes, e.status,
    e.created_at, COUNT(q.question_id) AS question_count,
    SUM(CASE WHEN q.question_id IS NOT NULL AND q.correct_option_id IS NOT NULL THEN 1 ELSE 0 END) AS keyed_questions,
    (SELECT STRING_AGG(CONCAT('Part ', p.part_number), ' • ') WITHIN GROUP (ORDER BY p.order_no)
     FROM dbo.exam_parts p WHERE p.exam_id = e.exam_id) AS parts
FROM dbo.exams e
LEFT JOIN dbo.exam_parts ep ON ep.exam_id = e.exam_id
LEFT JOIN dbo.questions q ON q.part_id = ep.part_id
GROUP BY e.exam_id, e.title, e.description, e.duration_minutes, e.status, e.created_at
ORDER BY e.created_at DESC";
                return Ok(await ReadDictionariesAsync(command));
            }
        }

        [AllowAnonymous]
        [HttpGet("exams/{examId:long}")]
        public async Task<IActionResult> GetExam(long examId)
        {
            var isAdmin = IsAdmin;
            using (var connection = await OpenConnectionAsync())
            {
                var exam = await ReadExamAsync(connection, examId);
                if (exam == null || (!isAdmin && !string.Equals((string)exam["status"], "published", StringComparison.OrdinalIgnoreCase)))
                {
                    return NotFound(new { message = "Không tìm thấy đề thi." });
                }

                exam["parts"] = await ReadExamPartsAsync(connection, examId, isAdmin);
                return Ok(exam);
            }
        }

        [Authorize(Roles = "admin")]
        [HttpPost("admin/import")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(21 * 1024 * 1024)]
        public async Task<IActionResult> Import(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "Chọn file Word cần import." });
            }

            if (file.Length > 20 * 1024 * 1024 || !string.Equals(Path.GetExtension(file.FileName), ".docx", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Chỉ hỗ trợ file .docx có dung lượng tối đa 20 MB." });
            }

            ImportedExam imported;
            try
            {
                using (var stream = file.OpenReadStream())
                {
                    imported = _parser.Parse(stream);
                }
            }
            catch (InvalidDataException exception)
            {
                return BadRequest(new { message = exception.Message });
            }

            if (imported.DurationMinutes < 1 || imported.DurationMinutes > short.MaxValue)
            {
                return BadRequest(new { message = "Thời lượng trong file không hợp lệ." });
            }

            using (var connection = await OpenConnectionAsync())
            using (var transaction = connection.BeginTransaction())
            {
                var examId = await InsertExamAsync(connection, transaction, imported, CurrentUserId);
                foreach (var part in imported.Parts)
                {
                    var partId = await InsertPartAsync(connection, transaction, examId, part);
                    var passageIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                    for (var index = 0; index < part.Passages.Count; index++)
                    {
                        var passage = part.Passages[index];
                        passageIds[passage.ExternalId] = await InsertPassageAsync(connection, transaction, partId, passage, index + 1);
                    }

                    for (var index = 0; index < part.Questions.Count; index++)
                    {
                        var question = part.Questions[index];
                        long? passageId = null;
                        if (!string.IsNullOrEmpty(question.PassageExternalId) && passageIds.ContainsKey(question.PassageExternalId))
                        {
                            passageId = passageIds[question.PassageExternalId];
                        }

                        var questionId = await InsertQuestionAsync(connection, transaction, partId, passageId, question, index + 1);
                        long? correctOptionId = null;
                        for (var optionIndex = 0; optionIndex < question.Options.Count; optionIndex++)
                        {
                            var option = question.Options[optionIndex];
                            var isCorrect = string.Equals(option.Label, question.CorrectLabel, StringComparison.OrdinalIgnoreCase);
                            var optionId = await InsertOptionAsync(connection, transaction, questionId, option, optionIndex + 1, isCorrect);
                            if (isCorrect)
                            {
                                correctOptionId = optionId;
                            }
                        }

                        if (correctOptionId.HasValue)
                        {
                            using (var command = connection.CreateCommand())
                            {
                                command.Transaction = transaction;
                                command.CommandText = "UPDATE dbo.questions SET correct_option_id = @optionId WHERE question_id = @id";
                                command.Parameters.AddWithValue("@optionId", correctOptionId.Value);
                                command.Parameters.AddWithValue("@id", questionId);
                                await command.ExecuteNonQueryAsync();
                            }
                        }
                    }
                }

                await InsertImportHistoryAsync(connection, transaction, examId, CurrentUserId, Path.GetFileName(file.FileName), Guid.NewGuid().ToString("N") + ".docx");
                transaction.Commit();
                var questionCount = imported.Parts.Sum(part => part.Questions.Count);
                var keyedQuestionCount = imported.Parts.Sum(part => part.Questions.Count(question => !string.IsNullOrEmpty(question.CorrectLabel)));
                return Ok(new
                {
                    examId = examId,
                    title = imported.Title,
                    durationMinutes = imported.DurationMinutes,
                    questionCount = questionCount,
                    keyedQuestionCount = keyedQuestionCount,
                    message = keyedQuestionCount == questionCount
                        ? "Đã import đề ở trạng thái bản nháp cùng đầy đủ đáp án đúng. Hãy rà soát trước khi xuất bản."
                        : "Đã import đề ở trạng thái bản nháp. Cần bổ sung đáp án đúng trước khi xuất bản."
                });
            }
        }

        [Authorize(Roles = "admin")]
        [HttpPut("admin/questions/{questionId:long}")]
        public async Task<IActionResult> UpdateQuestion(long questionId, [FromBody] UpdateQuestionRequest input)
        {
            if (input == null || string.IsNullOrWhiteSpace(input.Content) || input.Options == null ||
                input.Options.Count != 4 || input.Options.Select(option => option.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4 ||
                (!string.IsNullOrEmpty(input.CorrectLabel) &&
                 !input.Options.Any(option => string.Equals(option.Label, input.CorrectLabel, StringComparison.OrdinalIgnoreCase))))
            {
                return BadRequest(new { message = "Mỗi câu cần nội dung và đủ bốn lựa chọn A–D." });
            }

            if (!input.Options.Select(option => option.Label.ToUpperInvariant()).OrderBy(label => label)
                .SequenceEqual(new[] { "A", "B", "C", "D" }))
            {
                return BadRequest(new { message = "Nhãn lựa chọn phải là A, B, C và D." });
            }

            using (var connection = await OpenConnectionAsync())
            using (var transaction = connection.BeginTransaction())
            {
                long partId;
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"SELECT q.part_id
FROM dbo.questions q
INNER JOIN dbo.exam_parts p ON p.part_id = q.part_id
INNER JOIN dbo.exams e ON e.exam_id = p.exam_id
WHERE q.question_id = @id AND e.status = 'draft'
AND NOT EXISTS (SELECT 1 FROM dbo.exam_attempts a WHERE a.exam_id = e.exam_id)";
                    command.Parameters.AddWithValue("@id", questionId);
                    var result = await command.ExecuteScalarAsync();
                    if (result == null)
                    {
                        return Conflict(new { message = "Chỉ có thể chỉnh sửa câu hỏi trong đề nháp chưa có lượt làm bài." });
                    }

                    partId = Convert.ToInt64(result);
                }

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"UPDATE dbo.questions SET content_html = @content, correct_option_id = NULL
WHERE question_id = @id;
DELETE FROM dbo.answer_options WHERE question_id = @id";
                    command.Parameters.AddWithValue("@content", input.Content.Trim());
                    command.Parameters.AddWithValue("@id", questionId);
                    await command.ExecuteNonQueryAsync();
                }

                long correctOptionId = 0;
                for (var index = 0; index < input.Options.Count; index++)
                {
                    var option = input.Options[index];
                    var optionId = await InsertOptionAsync(connection, transaction, questionId,
                        new ImportedOption { Label = option.Label.ToUpperInvariant(), Content = option.Content ?? "" },
                        index + 1,
                        string.Equals(option.Label, input.CorrectLabel, StringComparison.OrdinalIgnoreCase));
                    if (string.Equals(option.Label, input.CorrectLabel, StringComparison.OrdinalIgnoreCase))
                    {
                        correctOptionId = optionId;
                    }
                }

                if (correctOptionId > 0)
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = "UPDATE dbo.questions SET correct_option_id = @optionId WHERE question_id = @id";
                        command.Parameters.AddWithValue("@optionId", correctOptionId);
                        command.Parameters.AddWithValue("@id", questionId);
                        await command.ExecuteNonQueryAsync();
                    }
                }

                transaction.Commit();
                return Ok(new { questionId = questionId, partId = partId, message = "Đã lưu câu hỏi và đáp án." });
            }
        }

        [Authorize(Roles = "admin")]
        [HttpPut("admin/exams/{examId:long}/publish")]
        public async Task<IActionResult> Publish(long examId)
        {
            using (var connection = await OpenConnectionAsync())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"IF NOT EXISTS (SELECT 1 FROM dbo.exams WHERE exam_id = @examId)
    SELECT -1
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.questions q
    INNER JOIN dbo.exam_parts p ON p.part_id = q.part_id
    WHERE p.exam_id = @examId)
    SELECT 0
ELSE IF EXISTS (SELECT 1 FROM dbo.questions q
    INNER JOIN dbo.exam_parts p ON p.part_id = q.part_id
    OUTER APPLY (SELECT COUNT(1) AS option_count FROM dbo.answer_options o WHERE o.question_id = q.question_id) options
    WHERE p.exam_id = @examId AND (q.correct_option_id IS NULL OR options.option_count <> 4))
    SELECT 0
ELSE
    SELECT 1";
                command.Parameters.AddWithValue("@examId", examId);
                var status = Convert.ToInt32(await command.ExecuteScalarAsync());
                if (status < 0)
                {
                    return NotFound(new { message = "Không tìm thấy đề thi." });
                }

                if (status == 0)
                {
                    return BadRequest(new { message = "Cần có câu hỏi, đủ bốn lựa chọn và đáp án đúng cho mọi câu trước khi xuất bản." });
                }

                command.CommandText = @"UPDATE dbo.exams SET status = 'published', published_at = SYSUTCDATETIME()
WHERE exam_id = @examId";
                await command.ExecuteNonQueryAsync();
                return Ok(new { message = "Đề thi đã được xuất bản." });
            }
        }

        [Authorize(Roles = "admin")]
        [HttpDelete("admin/exams/{examId:long}")]
        public async Task<IActionResult> DeleteExam(long examId)
        {
            using (var connection = await OpenConnectionAsync())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @status NVARCHAR(20);
SELECT @status = status FROM dbo.exams WITH (XLOCK, HOLDLOCK) WHERE exam_id = @id;

IF @status IS NULL
BEGIN
    COMMIT TRANSACTION;
    SELECT -1;
END
ELSE
BEGIN
    UPDATE dbo.exam_imports SET exam_id = NULL WHERE exam_id = @id;
    DELETE answers
        FROM dbo.attempt_answers answers
        INNER JOIN dbo.exam_attempts attempts ON attempts.attempt_id = answers.attempt_id
        WHERE attempts.exam_id = @id;
    DELETE FROM dbo.exam_attempts WHERE exam_id = @id;
    UPDATE q SET correct_option_id = NULL
        FROM dbo.questions q
        INNER JOIN dbo.exam_parts p ON p.part_id = q.part_id
        WHERE p.exam_id = @id;
    DELETE o FROM dbo.answer_options o
        INNER JOIN dbo.questions q ON q.question_id = o.question_id
        INNER JOIN dbo.exam_parts p ON p.part_id = q.part_id
        WHERE p.exam_id = @id;
    DELETE q FROM dbo.questions q
        INNER JOIN dbo.exam_parts p ON p.part_id = q.part_id
        WHERE p.exam_id = @id;
    DELETE passage FROM dbo.passages passage
        INNER JOIN dbo.exam_parts p ON p.part_id = passage.part_id
        WHERE p.exam_id = @id;
    DELETE FROM dbo.exam_parts WHERE exam_id = @id;
    DELETE FROM dbo.exams WHERE exam_id = @id;
    COMMIT TRANSACTION;
    SELECT 1;
END
";
                command.Parameters.AddWithValue("@id", examId);
                var result = Convert.ToInt32(await command.ExecuteScalarAsync());
                if (result < 0)
                {
                    return NotFound(new { message = "Không tìm thấy đề thi." });
                }

                return Ok(new { message = "Đã xóa đề thi và toàn bộ lượt làm bài, câu trả lời, kết quả liên quan." });
            }
        }

        [AllowAnonymous]
        [HttpPost("attempts/{examId:long}")]
        public async Task<IActionResult> StartAttempt(long examId)
        {
            var userId = User.Identity != null && User.Identity.IsAuthenticated
                ? (long?)CurrentUserId
                : null;
            var accessKey = userId.HasValue ? null : CreateAttemptKey();
            using (var connection = await OpenConnectionAsync())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"INSERT INTO dbo.exam_attempts (exam_id, user_id, access_key, duration_minutes_snapshot)
OUTPUT INSERTED.attempt_id, INSERTED.started_at
SELECT exam_id, @userId, @accessKey, duration_minutes FROM dbo.exams WHERE exam_id = @examId AND status = 'published'";
                command.Parameters.AddWithValue("@examId", examId);
                command.Parameters.Add("@userId", SqlDbType.BigInt).Value = (object)userId ?? DBNull.Value;
                command.Parameters.Add("@accessKey", SqlDbType.NVarChar, 64).Value = (object)accessKey ?? DBNull.Value;
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (!await reader.ReadAsync())
                    {
                        return NotFound(new { message = "Đề thi không còn khả dụng." });
                    }

                    return Ok(new
                    {
                        attemptId = reader.GetInt64(0),
                        startedAt = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc),
                        attemptKey = accessKey
                    });
                }
            }
        }

        [AllowAnonymous]
        [HttpPut("attempts/{attemptId:long}/answers")]
        public async Task<IActionResult> SaveAnswer(long attemptId, [FromBody] SaveAnswerRequest input)
        {
            if (input == null)
            {
                return BadRequest(new { message = "Thiếu câu trả lời." });
            }

            using (var connection = await OpenConnectionAsync())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"IF EXISTS (SELECT 1 FROM dbo.exam_attempts a
    INNER JOIN dbo.exam_parts p ON p.exam_id = a.exam_id
    INNER JOIN dbo.questions q ON q.part_id = p.part_id
    LEFT JOIN dbo.answer_options o ON o.question_id = q.question_id AND o.option_id = @optionId
    WHERE a.attempt_id = @attemptId
      AND ((@userId IS NOT NULL AND a.user_id = @userId)
        OR (@userId IS NULL AND a.user_id IS NULL AND a.access_key = @accessKey))
      AND a.status = 'in_progress' AND q.question_id = @questionId
      AND (@optionId IS NULL OR o.option_id IS NOT NULL))
BEGIN
    MERGE dbo.attempt_answers AS target
    USING (SELECT @attemptId AS attempt_id, @questionId AS question_id) AS source
    ON target.attempt_id = source.attempt_id AND target.question_id = source.question_id
    WHEN MATCHED THEN UPDATE SET selected_option_id = @optionId, answered_at = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT (attempt_id, question_id, selected_option_id, answered_at)
        VALUES (@attemptId, @questionId, @optionId, SYSUTCDATETIME());
    SELECT 1
END
ELSE SELECT 0";
                command.Parameters.AddWithValue("@attemptId", attemptId);
                AddAttemptOwnerParameters(command);
                command.Parameters.AddWithValue("@questionId", input.QuestionId);
                command.Parameters.Add("@optionId", SqlDbType.BigInt).Value = (object)input.SelectedOptionId ?? DBNull.Value;
                if (Convert.ToInt32(await command.ExecuteScalarAsync()) == 0)
                {
                    return Conflict(new { message = "Không thể lưu câu trả lời cho lượt thi này." });
                }

                return Ok(new { saved = true });
            }
        }

        [AllowAnonymous]
        [HttpPost("attempts/{attemptId:long}/submit")]
        public async Task<IActionResult> SubmitAttempt(long attemptId)
        {
            using (var connection = await OpenConnectionAsync())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"DECLARE @examId BIGINT;
SELECT @examId = exam_id FROM dbo.exam_attempts
WHERE attempt_id = @attemptId
  AND ((@userId IS NOT NULL AND user_id = @userId)
    OR (@userId IS NULL AND user_id IS NULL AND access_key = @accessKey))
  AND status = 'in_progress';
IF @examId IS NULL SELECT -1;
ELSE IF EXISTS (SELECT 1 FROM dbo.questions q
    INNER JOIN dbo.exam_parts p ON p.part_id = q.part_id
    WHERE p.exam_id = @examId AND q.correct_option_id IS NULL) SELECT -2;
ELSE
BEGIN
    DECLARE @total INT, @correct INT;
    SELECT @total = COUNT(1) FROM dbo.questions q
        INNER JOIN dbo.exam_parts p ON p.part_id = q.part_id WHERE p.exam_id = @examId;
    SELECT @correct = COUNT(1) FROM dbo.attempt_answers a
        INNER JOIN dbo.questions q ON q.question_id = a.question_id
        INNER JOIN dbo.exam_parts p ON p.part_id = q.part_id
        WHERE a.attempt_id = @attemptId AND p.exam_id = @examId
          AND a.selected_option_id = q.correct_option_id;
    UPDATE dbo.attempt_answers SET is_correct_snapshot =
        CASE WHEN selected_option_id = (SELECT correct_option_id FROM dbo.questions WHERE question_id = attempt_answers.question_id) THEN 1 ELSE 0 END
        WHERE attempt_id = @attemptId;
    UPDATE dbo.exam_attempts SET status = 'submitted', submitted_at = SYSUTCDATETIME(),
        correct_count = @correct, total_questions = @total,
        score_percent = CASE WHEN @total = 0 THEN 0 ELSE CAST(100.0 * @correct / @total AS DECIMAL(5,2)) END
        WHERE attempt_id = @attemptId;
    SELECT @correct;
END";
                command.Parameters.AddWithValue("@attemptId", attemptId);
                AddAttemptOwnerParameters(command);
                var result = Convert.ToInt32(await command.ExecuteScalarAsync());
                if (result < 0)
                {
                    return NotFound(new { message = "Không tìm thấy lượt thi đang làm." });
                }

                if (result == -2)
                {
                    return BadRequest(new { message = "Đề thi chưa có đầy đủ đáp án đúng nên chưa thể chấm điểm." });
                }

                return await GetResult(attemptId);
            }
        }

        [AllowAnonymous]
        [HttpGet("attempts/{attemptId:long}/result")]
        public async Task<IActionResult> GetResult(long attemptId)
        {
            using (var connection = await OpenConnectionAsync())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT a.attempt_id, e.title, a.started_at, a.submitted_at,
    a.correct_count, a.total_questions, a.score_percent, a.status
FROM dbo.exam_attempts a INNER JOIN dbo.exams e ON e.exam_id = a.exam_id
WHERE a.attempt_id = @attemptId
  AND ((@userId IS NOT NULL AND a.user_id = @userId)
    OR (@userId IS NULL AND a.user_id IS NULL AND a.access_key = @accessKey))";
                command.Parameters.AddWithValue("@attemptId", attemptId);
                AddAttemptOwnerParameters(command);
                Dictionary<string, object> attempt;
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (!await reader.ReadAsync())
                    {
                        return NotFound(new { message = "Không tìm thấy kết quả." });
                    }

                    attempt = MapRow(reader);
                }

                if (!string.Equals(Convert.ToString(attempt["status"]), "submitted", StringComparison.OrdinalIgnoreCase))
                {
                    return Conflict(new { message = "Lượt thi chưa được nộp." });
                }

                command.Parameters.Clear();
                command.CommandText = @"SELECT q.question_number, q.content_html,
    chosen.label AS selected_label, correct.label AS correct_label, q.correct_option_id
FROM dbo.exam_attempts a
INNER JOIN dbo.exam_parts p ON p.exam_id = a.exam_id
INNER JOIN dbo.questions q ON q.part_id = p.part_id
LEFT JOIN dbo.attempt_answers answer ON answer.attempt_id = a.attempt_id AND answer.question_id = q.question_id
LEFT JOIN dbo.answer_options chosen ON chosen.option_id = answer.selected_option_id
LEFT JOIN dbo.answer_options correct ON correct.option_id = q.correct_option_id
WHERE a.attempt_id = @attemptId
  AND ((@userId IS NOT NULL AND a.user_id = @userId)
    OR (@userId IS NULL AND a.user_id IS NULL AND a.access_key = @accessKey))
ORDER BY p.order_no, q.order_no";
                command.Parameters.AddWithValue("@attemptId", attemptId);
                AddAttemptOwnerParameters(command);
                attempt["answers"] = await ReadDictionariesAsync(command);
                return Ok(attempt);
            }
        }

        private void AddAttemptOwnerParameters(SqlCommand command)
        {
            var userId = User.Identity != null && User.Identity.IsAuthenticated
                ? (long?)CurrentUserId
                : null;
            command.Parameters.Add("@userId", SqlDbType.BigInt).Value = (object)userId ?? DBNull.Value;
            command.Parameters.Add("@accessKey", SqlDbType.NVarChar, 64).Value =
                (object)Request.Headers["X-Attempt-Key"].FirstOrDefault() ?? DBNull.Value;
        }

        private static string CreateAttemptKey()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        private long CurrentUserId
        {
            get
            {
                long userId;
                if (!long.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out userId))
                {
                    throw new UnauthorizedAccessException("The authenticated token has no valid user id.");
                }

                return userId;
            }
        }

        private bool IsAdmin
        {
            get { return User.IsInRole("admin"); }
        }

        private string CreateToken(long userId, string fullName, string email, string role)
        {
            var keyValue = _configuration["Authentication:JwtBearer:SecurityKey"];
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyValue));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, fullName),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Role, role)
            };
            var token = new JwtSecurityToken(
                _configuration["Authentication:JwtBearer:Issuer"],
                _configuration["Authentication:JwtBearer:Audience"],
                claims,
                expires: DateTime.UtcNow.AddHours(8),
                signingCredentials: credentials);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private async Task<SqlConnection> OpenConnectionAsync()
        {
            var connection = new SqlConnection(_configuration.GetConnectionString("Default"));
            await connection.OpenAsync();
            return connection;
        }

        private static async Task<long> InsertExamAsync(SqlConnection connection, SqlTransaction transaction, ImportedExam exam, long userId)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO dbo.exams (title, duration_minutes, status, created_by)
OUTPUT INSERTED.exam_id VALUES (@title, @duration, 'draft', @userId)";
                command.Parameters.AddWithValue("@title", exam.Title);
                command.Parameters.AddWithValue("@duration", exam.DurationMinutes);
                command.Parameters.AddWithValue("@userId", userId);
                return Convert.ToInt64(await command.ExecuteScalarAsync());
            }
        }

        private static async Task<long> InsertPartAsync(SqlConnection connection, SqlTransaction transaction, long examId, ImportedPart part)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO dbo.exam_parts (exam_id, part_number, title, directions, question_mode, order_no)
OUTPUT INSERTED.part_id VALUES (@examId, @number, @title, @directions, @mode, @order)";
                command.Parameters.AddWithValue("@examId", examId);
                command.Parameters.AddWithValue("@number", part.Number);
                command.Parameters.AddWithValue("@title", part.Title);
                command.Parameters.AddWithValue("@directions", (object)part.Directions ?? DBNull.Value);
                command.Parameters.AddWithValue("@mode", part.Mode);
                command.Parameters.AddWithValue("@order", part.Number);
                return Convert.ToInt64(await command.ExecuteScalarAsync());
            }
        }

        private static async Task<long> InsertPassageAsync(SqlConnection connection, SqlTransaction transaction, long partId, ImportedPassage passage, int order)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO dbo.passages (part_id, passage_number, passage_type, content_html, order_no)
OUTPUT INSERTED.passage_id VALUES (@partId, @number, @type, @content, @order)";
                command.Parameters.AddWithValue("@partId", partId);
                command.Parameters.AddWithValue("@number", (object)passage.ExternalId ?? DBNull.Value);
                command.Parameters.AddWithValue("@type", (object)passage.Type ?? DBNull.Value);
                command.Parameters.AddWithValue("@content", passage.Content ?? "");
                command.Parameters.AddWithValue("@order", order);
                return Convert.ToInt64(await command.ExecuteScalarAsync());
            }
        }

        private static async Task<long> InsertQuestionAsync(SqlConnection connection, SqlTransaction transaction, long partId, long? passageId, ImportedQuestion question, int order)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO dbo.questions (part_id, passage_id, question_number, content_html, order_no)
OUTPUT INSERTED.question_id VALUES (@partId, @passageId, @number, @content, @order)";
                command.Parameters.AddWithValue("@partId", partId);
                command.Parameters.AddWithValue("@passageId", (object)passageId ?? DBNull.Value);
                command.Parameters.AddWithValue("@number", question.Number);
                command.Parameters.AddWithValue("@content", question.Content ?? "");
                command.Parameters.AddWithValue("@order", order);
                return Convert.ToInt64(await command.ExecuteScalarAsync());
            }
        }

        private static async Task<long> InsertOptionAsync(SqlConnection connection, SqlTransaction transaction, long questionId, ImportedOption option, int order, bool isCorrect)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO dbo.answer_options (question_id, label, content_html, order_no, is_correct)
OUTPUT INSERTED.option_id VALUES (@questionId, @label, @content, @order, @isCorrect)";
                command.Parameters.AddWithValue("@questionId", questionId);
                command.Parameters.AddWithValue("@label", option.Label);
                command.Parameters.AddWithValue("@content", option.Content ?? "");
                command.Parameters.AddWithValue("@order", order);
                command.Parameters.AddWithValue("@isCorrect", isCorrect);
                return Convert.ToInt64(await command.ExecuteScalarAsync());
            }
        }

        private static async Task InsertImportHistoryAsync(SqlConnection connection, SqlTransaction transaction, long examId, long userId, string fileName, string storageKey)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO dbo.exam_imports
(exam_id, uploaded_by, original_filename, storage_key, status)
VALUES (@examId, @userId, @filename, @storageKey, 'success')";
                command.Parameters.AddWithValue("@examId", examId);
                command.Parameters.AddWithValue("@userId", userId);
                command.Parameters.AddWithValue("@filename", fileName);
                command.Parameters.AddWithValue("@storageKey", storageKey);
                await command.ExecuteNonQueryAsync();
            }
        }

        private static async Task<Dictionary<string, object>> ReadExamAsync(SqlConnection connection, long examId)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT e.exam_id, e.title, e.description, e.duration_minutes, e.status,
e.created_at, COUNT(q.question_id) AS question_count
FROM dbo.exams e
LEFT JOIN dbo.exam_parts p ON p.exam_id = e.exam_id
LEFT JOIN dbo.questions q ON q.part_id = p.part_id
WHERE e.exam_id = @id
GROUP BY e.exam_id, e.title, e.description, e.duration_minutes, e.status, e.created_at";
                command.Parameters.AddWithValue("@id", examId);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    return await reader.ReadAsync() ? MapRow(reader) : null;
                }
            }
        }

        private static async Task<List<Dictionary<string, object>>> ReadExamPartsAsync(SqlConnection connection, long examId, bool includeAnswers)
        {
            var parts = new List<Dictionary<string, object>>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT part_id, part_number, title, directions, question_mode, order_no
FROM dbo.exam_parts WHERE exam_id = @id ORDER BY order_no";
                command.Parameters.AddWithValue("@id", examId);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        parts.Add(MapRow(reader));
                    }
                }
            }

            foreach (var part in parts)
            {
                var partId = Convert.ToInt64(part["part_id"]);
                var passages = new List<Dictionary<string, object>>();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"SELECT passage_id, passage_number, title, passage_type, content_html, order_no
FROM dbo.passages WHERE part_id = @id ORDER BY order_no";
                    command.Parameters.AddWithValue("@id", partId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            passages.Add(MapRow(reader));
                        }
                    }
                }

                var questions = new List<Dictionary<string, object>>();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"SELECT question_id, passage_id, question_number, content_html, correct_option_id, order_no
FROM dbo.questions WHERE part_id = @id ORDER BY order_no";
                    command.Parameters.AddWithValue("@id", partId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            questions.Add(MapRow(reader));
                        }
                    }
                }

                foreach (var question in questions)
                {
                    var questionId = Convert.ToInt64(question["question_id"]);
                    using (var optionsCommand = connection.CreateCommand())
                    {
                        optionsCommand.CommandText = @"SELECT option_id, label, content_html, order_no
FROM dbo.answer_options WHERE question_id = @id ORDER BY order_no";
                        optionsCommand.Parameters.AddWithValue("@id", questionId);
                        question["options"] = await ReadDictionariesAsync(optionsCommand);
                    }

                    if (!includeAnswers)
                    {
                        question.Remove("correct_option_id");
                    }
                }

                part["passages"] = passages;
                part["questions"] = questions;
            }

            return parts;
        }

        private static async Task<List<Dictionary<string, object>>> ReadDictionariesAsync(SqlCommand command)
        {
            var rows = new List<Dictionary<string, object>>();
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    rows.Add(MapRow(reader));
                }
            }

            return rows;
        }

        private static Dictionary<string, object> MapRow(IDataRecord reader)
        {
            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                if (reader.IsDBNull(index))
                {
                    row[reader.GetName(index)] = null;
                }
                else
                {
                    var value = reader.GetValue(index);
                    row[reader.GetName(index)] = value is DateTime
                        ? DateTime.SpecifyKind((DateTime)value, DateTimeKind.Utc)
                        : value;
                }
            }

            return row;
        }
    }

    public class LoginRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class UpdateQuestionRequest
    {
        public string Content { get; set; }
        public string CorrectLabel { get; set; }
        public List<UpdateOptionRequest> Options { get; set; }
    }

    public class UpdateOptionRequest
    {
        public string Label { get; set; }
        public string Content { get; set; }
    }

    public class SaveAnswerRequest
    {
        public long QuestionId { get; set; }
        public long? SelectedOptionId { get; set; }
    }
}
