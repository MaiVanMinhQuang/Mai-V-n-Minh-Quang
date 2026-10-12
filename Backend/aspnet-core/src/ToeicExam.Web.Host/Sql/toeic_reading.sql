IF OBJECT_ID(N'dbo.users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.users (
        user_id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        full_name NVARCHAR(150) NOT NULL,
        email NVARCHAR(254) NOT NULL UNIQUE,
        password_hash NVARCHAR(255) NOT NULL,
        role VARCHAR(10) NOT NULL CONSTRAINT DF_users_role DEFAULT 'user',
        is_active BIT NOT NULL CONSTRAINT DF_users_active DEFAULT 1,
        created_at DATETIME2 NOT NULL CONSTRAINT DF_users_created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_users_role CHECK (role IN ('admin', 'user'))
    );
END;

IF OBJECT_ID(N'dbo.exams', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.exams (
        exam_id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        title NVARCHAR(255) NOT NULL,
        description NVARCHAR(MAX) NULL,
        duration_minutes SMALLINT NOT NULL,
        status VARCHAR(12) NOT NULL CONSTRAINT DF_exams_status DEFAULT 'draft',
        created_by BIGINT NOT NULL,
        version SMALLINT NOT NULL CONSTRAINT DF_exams_version DEFAULT 1,
        created_at DATETIME2 NOT NULL CONSTRAINT DF_exams_created DEFAULT SYSUTCDATETIME(),
        published_at DATETIME2 NULL,
        CONSTRAINT CK_exams_duration CHECK (duration_minutes > 0),
        CONSTRAINT CK_exams_status CHECK (status IN ('draft', 'published', 'archived')),
        CONSTRAINT FK_exams_creator FOREIGN KEY (created_by) REFERENCES dbo.users(user_id)
    );
END;

IF OBJECT_ID(N'dbo.exam_imports', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.exam_imports (
        import_id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        exam_id BIGINT NULL,
        uploaded_by BIGINT NOT NULL,
        original_filename NVARCHAR(255) NOT NULL,
        storage_key NVARCHAR(1024) NOT NULL,
        status VARCHAR(12) NOT NULL CONSTRAINT DF_imports_status DEFAULT 'uploaded',
        error_message NVARCHAR(MAX) NULL,
        created_at DATETIME2 NOT NULL CONSTRAINT DF_imports_created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_imports_exam FOREIGN KEY (exam_id) REFERENCES dbo.exams(exam_id) ON DELETE SET NULL,
        CONSTRAINT FK_imports_uploader FOREIGN KEY (uploaded_by) REFERENCES dbo.users(user_id)
    );
END;

IF OBJECT_ID(N'dbo.exam_parts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.exam_parts (
        part_id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        exam_id BIGINT NOT NULL,
        part_number TINYINT NOT NULL,
        title NVARCHAR(150) NOT NULL,
        directions NVARCHAR(MAX) NULL,
        question_mode VARCHAR(20) NOT NULL,
        order_no SMALLINT NOT NULL,
        CONSTRAINT UQ_parts_number UNIQUE (exam_id, part_number),
        CONSTRAINT UQ_parts_order UNIQUE (exam_id, order_no),
        CONSTRAINT FK_parts_exam FOREIGN KEY (exam_id) REFERENCES dbo.exams(exam_id) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.passages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.passages (
        passage_id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        part_id BIGINT NOT NULL,
        passage_number NVARCHAR(30) NULL,
        title NVARCHAR(255) NULL,
        passage_type NVARCHAR(50) NULL,
        content_html NVARCHAR(MAX) NOT NULL,
        order_no SMALLINT NOT NULL,
        CONSTRAINT UQ_passages_order UNIQUE (part_id, order_no),
        CONSTRAINT UQ_passages_part_id UNIQUE (part_id, passage_id),
        CONSTRAINT FK_passages_part FOREIGN KEY (part_id) REFERENCES dbo.exam_parts(part_id) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.questions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.questions (
        question_id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        part_id BIGINT NOT NULL,
        passage_id BIGINT NULL,
        question_number SMALLINT NOT NULL,
        content_html NVARCHAR(MAX) NOT NULL,
        correct_option_id BIGINT NULL,
        order_no SMALLINT NOT NULL,
        CONSTRAINT UQ_questions_number UNIQUE (part_id, question_number),
        CONSTRAINT UQ_questions_order UNIQUE (part_id, order_no),
        CONSTRAINT UQ_questions_part_id UNIQUE (part_id, question_id),
        CONSTRAINT FK_questions_part FOREIGN KEY (part_id) REFERENCES dbo.exam_parts(part_id) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.answer_options', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.answer_options (
        option_id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        question_id BIGINT NOT NULL,
        label CHAR(1) NOT NULL,
        content_html NVARCHAR(MAX) NOT NULL,
        order_no TINYINT NOT NULL,
        is_correct BIT NOT NULL CONSTRAINT DF_options_correct DEFAULT 0,
        CONSTRAINT UQ_options_label UNIQUE (question_id, label),
        CONSTRAINT UQ_options_order UNIQUE (question_id, order_no),
        CONSTRAINT UQ_options_question_id UNIQUE (question_id, option_id),
        CONSTRAINT CK_options_label CHECK (label IN ('A', 'B', 'C', 'D')),
        CONSTRAINT FK_options_question FOREIGN KEY (question_id) REFERENCES dbo.questions(question_id) ON DELETE CASCADE
    );
END;

IF COL_LENGTH(N'dbo.questions', N'correct_option_id') IS NULL
    ALTER TABLE dbo.questions ADD correct_option_id BIGINT NULL;

IF COL_LENGTH(N'dbo.answer_options', N'is_correct') IS NULL
    ALTER TABLE dbo.answer_options ADD is_correct BIT NULL CONSTRAINT DF_options_correct_existing DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_questions_correct_option')
    ALTER TABLE dbo.questions ADD CONSTRAINT FK_questions_correct_option
        FOREIGN KEY (correct_option_id) REFERENCES dbo.answer_options(option_id) ON DELETE SET NULL;

IF OBJECT_ID(N'dbo.exam_attempts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.exam_attempts (
        attempt_id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        exam_id BIGINT NOT NULL,
        user_id BIGINT NULL,
        access_key NVARCHAR(64) NULL,
        status VARCHAR(12) NOT NULL CONSTRAINT DF_attempt_status DEFAULT 'in_progress',
        started_at DATETIME2 NOT NULL CONSTRAINT DF_attempt_started DEFAULT SYSUTCDATETIME(),
        submitted_at DATETIME2 NULL,
        duration_minutes_snapshot SMALLINT NOT NULL,
        correct_count SMALLINT NULL,
        total_questions SMALLINT NULL,
        score_percent DECIMAL(5,2) NULL,
        CONSTRAINT CK_attempt_score CHECK (score_percent IS NULL OR score_percent BETWEEN 0 AND 100),
        CONSTRAINT FK_attempts_exam FOREIGN KEY (exam_id) REFERENCES dbo.exams(exam_id),
        CONSTRAINT FK_attempts_user FOREIGN KEY (user_id) REFERENCES dbo.users(user_id)
    );
END;

IF COL_LENGTH(N'dbo.exam_attempts', N'user_id') IS NOT NULL
    ALTER TABLE dbo.exam_attempts ALTER COLUMN user_id BIGINT NULL;

IF COL_LENGTH(N'dbo.exam_attempts', N'access_key') IS NULL
    ALTER TABLE dbo.exam_attempts ADD access_key NVARCHAR(64) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_exam_attempts_access_key' AND object_id = OBJECT_ID(N'dbo.exam_attempts'))
    EXEC(N'CREATE UNIQUE INDEX UX_exam_attempts_access_key ON dbo.exam_attempts(access_key) WHERE access_key IS NOT NULL');

IF OBJECT_ID(N'dbo.attempt_answers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.attempt_answers (
        attempt_id BIGINT NOT NULL,
        question_id BIGINT NOT NULL,
        selected_option_id BIGINT NULL,
        is_correct_snapshot BIT NULL,
        answered_at DATETIME2 NULL,
        CONSTRAINT PK_attempt_answers PRIMARY KEY (attempt_id, question_id),
        CONSTRAINT FK_answers_attempt FOREIGN KEY (attempt_id) REFERENCES dbo.exam_attempts(attempt_id) ON DELETE CASCADE,
        CONSTRAINT FK_answers_question FOREIGN KEY (question_id) REFERENCES dbo.questions(question_id),
        CONSTRAINT FK_answers_option FOREIGN KEY (question_id, selected_option_id) REFERENCES dbo.answer_options(question_id, option_id)
    );
END;
