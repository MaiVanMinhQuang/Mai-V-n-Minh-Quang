import 'dart:async';

import 'package:flutter/material.dart';

import 'api.dart';

void main() => runApp(const ToeicReadingApp());

class ToeicReadingApp extends StatelessWidget {
  const ToeicReadingApp({super.key});

  @override
  Widget build(BuildContext context) {
    const primary = Color(0xFF2463EB);
    const ink = Color(0xFF17243A);
    const canvas = Color(0xFFF6F8FC);
    return MaterialApp(
      title: 'TOEIC Reading',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: primary, surface: canvas),
        useMaterial3: true,
        scaffoldBackgroundColor: canvas,
        appBarTheme: const AppBarTheme(
          backgroundColor: Colors.white,
          foregroundColor: ink,
          surfaceTintColor: Colors.white,
          elevation: 0,
          centerTitle: false,
          titleTextStyle: TextStyle(
            color: ink,
            fontSize: 18,
            fontWeight: FontWeight.w700,
          ),
        ),
        cardTheme: CardThemeData(
          color: Colors.white,
          surfaceTintColor: Colors.white,
          elevation: 0,
          margin: EdgeInsets.zero,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(18),
            side: const BorderSide(color: Color(0xFFE8EDF5)),
          ),
        ),
        filledButtonTheme: FilledButtonThemeData(
          style: FilledButton.styleFrom(
            backgroundColor: primary,
            foregroundColor: Colors.white,
            minimumSize: const Size(48, 48),
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(12),
            ),
            textStyle: const TextStyle(fontWeight: FontWeight.w700),
          ),
        ),
        outlinedButtonTheme: OutlinedButtonThemeData(
          style: OutlinedButton.styleFrom(
            foregroundColor: ink,
            minimumSize: const Size(48, 46),
            side: const BorderSide(color: Color(0xFFD7DFEC)),
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(12),
            ),
          ),
        ),
        textTheme: ThemeData.light().textTheme.apply(
          bodyColor: ink,
          displayColor: ink,
        ),
      ),
      home: const ExamListPage(),
    );
  }
}

class ExamListPage extends StatefulWidget {
  const ExamListPage({super.key});

  @override
  State<ExamListPage> createState() => _ExamListPageState();
}

class _ExamListPageState extends State<ExamListPage> {
  final ToeicApi _api = ToeicApi();
  late Future<List<dynamic>> _exams;

  @override
  void initState() {
    super.initState();
    _exams = _loadExams();
  }

  Future<List<dynamic>> _loadExams() async {
    final response = await _api.get('/exams');
    if (response is! List) {
      throw const FormatException('Dữ liệu danh sách đề thi không hợp lệ.');
    }
    return response;
  }

  Future<void> _refresh() async {
    final future = _loadExams();
    setState(() {
      _exams = future;
    });
    await future;
  }

  @override
  void dispose() {
    _api.close();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const _BrandTitle(),
        actions: [
          IconButton(
            tooltip: 'Tải lại',
            onPressed: _refresh,
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: _refresh,
        child: FutureBuilder<List<dynamic>>(
          future: _exams,
          builder: (context, snapshot) {
            if (snapshot.connectionState == ConnectionState.waiting) {
              return const Center(child: CircularProgressIndicator());
            }
            if (snapshot.hasError) {
              return _MessageView(
                message: 'Không tải được đề thi.\n${snapshot.error}',
                onRetry: _refresh,
              );
            }

            final exams = snapshot.data ?? [];
            if (exams.isEmpty) {
              return const _MessageView(
                message: 'Chưa có đề thi được xuất bản. Hãy quay lại sau nhé.',
              );
            }

            return ListView(
              physics: const AlwaysScrollableScrollPhysics(),
              padding: const EdgeInsets.fromLTRB(20, 24, 20, 36),
              children: [
                Center(
                  child: ConstrainedBox(
                    constraints: const BoxConstraints(maxWidth: 900),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        const Text(
                          'Chào mừng bạn đến với TOEIC Reading',
                          style: TextStyle(
                            fontSize: 27,
                            fontWeight: FontWeight.w800,
                          ),
                        ),
                        const SizedBox(height: 8),
                        Text(
                          'Chọn một đề thi và bắt đầu luyện tập. Không cần đăng nhập.',
                          style: TextStyle(
                            color: Theme.of(context)
                                .colorScheme
                                .onSurfaceVariant,
                            fontSize: 15,
                          ),
                        ),
                        const SizedBox(height: 24),
                        _PracticeBanner(examCount: exams.length),
                        const SizedBox(height: 28),
                        Row(
                          children: [
                            const Expanded(
                              child: Text(
                                'Đề thi dành cho bạn',
                                style: TextStyle(
                                  fontSize: 20,
                                  fontWeight: FontWeight.w800,
                                ),
                              ),
                            ),
                            _CountBadge(count: exams.length),
                          ],
                        ),
                        const SizedBox(height: 14),
                        for (final raw in exams)
                          if (raw is Map<String, dynamic>)
                            _ExamCard(exam: raw, onTap: () => _openExam(raw)),
                      ],
                    ),
                  ),
                ),
              ],
            );
          },
        ),
      ),
    );
  }

  Future<void> _openExam(Map<String, dynamic> summary) async {
    final examId = summary['exam_id'];
    if (examId == null) return;
    try {
      final exam = await _api.get('/exams/$examId');
      if (!mounted || exam is! Map<String, dynamic>) return;
      await Navigator.of(context).push(
        MaterialPageRoute(
          builder: (_) => ExamPage(api: _api, exam: exam),
        ),
      );
    } catch (error) {
      if (mounted) _showError(context, error);
    }
  }
}

class ExamPage extends StatefulWidget {
  const ExamPage({required this.api, required this.exam, super.key});

  final ToeicApi api;
  final Map<String, dynamic> exam;

  @override
  State<ExamPage> createState() => _ExamPageState();
}

class _ExamPageState extends State<ExamPage> {
  final List<Map<String, dynamic>> _questions = [];
  final Map<int, int> _answers = {};
  int _current = 0;
  int? _attemptId;
  String? _attemptKey;
  DateTime? _startedAt;
  Timer? _timer;
  int _secondsRemaining = 0;
  bool _saving = false;
  bool _submitting = false;

  @override
  void initState() {
    super.initState();
    final parts = widget.exam['parts'];
    if (parts is List) {
      for (final rawPart in parts) {
        if (rawPart is! Map<String, dynamic>) continue;
        final partQuestions = rawPart['questions'];
        if (partQuestions is! List) continue;
        for (final rawQuestion in partQuestions) {
          if (rawQuestion is Map<String, dynamic>) {
            _questions.add({
              ...rawQuestion,
              'part_number': rawPart['part_number'],
            });
          }
        }
      }
    }
  }

  Future<void> _start() async {
    try {
      final result = await widget.api.post(
        '/attempts/${widget.exam['exam_id']}',
      );
      if (result is! Map<String, dynamic>) {
        throw const FormatException('Không nhận được thông tin lượt thi.');
      }
      setState(() {
        _attemptId = (result['attemptId'] as num).toInt();
        _attemptKey = result['attemptKey'] as String?;
        _startedAt = DateTime.parse(result['startedAt'] as String).toLocal();
        _secondsRemaining =
            (widget.exam['duration_minutes'] as num).toInt() * 60;
      });
      _timer = Timer.periodic(const Duration(seconds: 1), (_) => _tick());
    } catch (error) {
      if (mounted) _showError(context, error);
    }
  }

  Future<void> _choose(int questionId, int optionId) async {
    final previousAnswer = _answers[questionId];
    setState(() {
      _answers[questionId] = optionId;
      _saving = true;
    });
    try {
      await widget.api.put(
        '/attempts/$_attemptId/answers',
        attemptKey: _attemptKey,
        body: {'questionId': questionId, 'selectedOptionId': optionId},
      );
    } catch (error) {
      if (mounted) {
        setState(() {
          if (previousAnswer == null) {
            _answers.remove(questionId);
          } else {
            _answers[questionId] = previousAnswer;
          }
        });
        _showError(context, error);
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _submit({bool expired = false}) async {
    if (_attemptId == null || _submitting) return;
    var confirmed = expired;
    if (!expired) {
      confirmed =
          await showDialog<bool>(
            context: context,
            builder: (context) => AlertDialog(
              title: const Text('Nộp bài?'),
              content: const Text('Bạn có chắc muốn nộp bài thi không?'),
              actions: [
                TextButton(
                  onPressed: () => Navigator.pop(context, false),
                  child: const Text('Tiếp tục làm'),
                ),
                FilledButton(
                  onPressed: () => Navigator.pop(context, true),
                  child: const Text('Nộp bài'),
                ),
              ],
            ),
          ) ??
          false;
    }
    if (!confirmed) {
      return;
    }

    if (!mounted) return;
    setState(() => _submitting = true);
    _timer?.cancel();
    try {
      final result = await widget.api.post(
        '/attempts/$_attemptId/submit',
        attemptKey: _attemptKey,
      );
      if (!mounted) return;
      if (result is! Map<String, dynamic>) {
        throw const FormatException('Không nhận được kết quả sau khi nộp bài.');
      }
      await Navigator.of(context).pushReplacement(
        MaterialPageRoute(builder: (_) => ResultPage(result: result)),
      );
    } catch (error) {
      if (mounted) {
        setState(() => _submitting = false);
        _showError(context, error);
      }
      if (_startedAt != null && mounted) {
        _timer = Timer.periodic(const Duration(seconds: 1), (_) => _tick());
      }
    }
  }

  void _tick() {
    final started = _startedAt;
    if (started == null || !mounted) return;
    final duration = (widget.exam['duration_minutes'] as num).toInt() * 60;
    final remaining = duration - DateTime.now().difference(started).inSeconds;
    if (remaining <= 0) {
      if (_saving) {
        setState(() => _secondsRemaining = 0);
      } else {
        _timer?.cancel();
        _submit(expired: true);
      }
    } else {
      setState(() => _secondsRemaining = remaining);
    }
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (_questions.isEmpty) {
      return Scaffold(
        appBar: AppBar(),
        body: const _MessageView(message: 'Đề thi hiện chưa có câu hỏi.'),
      );
    }
    final question = _questions[_current];
    final questionId = (question['question_id'] as num).toInt();
    final options = question['options'] is List
        ? (question['options'] as List)
              .whereType<Map<String, dynamic>>()
              .toList()
        : <Map<String, dynamic>>[];

    return Scaffold(
      appBar: AppBar(
        title: Text(widget.exam['title']?.toString() ?? 'Làm bài'),
      ),
      body: _attemptId == null
          ? _ExamIntroduction(
              exam: widget.exam,
              questionCount: _questions.length,
              onStart: _start,
            )
          : Column(
              children: [
                Container(
                  color: Colors.white,
                  padding: const EdgeInsets.fromLTRB(18, 12, 18, 14),
                  child: Column(
                    children: [
                      Row(
                        children: [
                          Expanded(
                            child: Text(
                              'Câu ${_current + 1} / ${_questions.length}',
                              style: const TextStyle(
                                color: Color(0xFF17243A),
                                fontWeight: FontWeight.w800,
                              ),
                            ),
                          ),
                          Container(
                            padding: const EdgeInsets.symmetric(
                              horizontal: 12,
                              vertical: 7,
                            ),
                            decoration: BoxDecoration(
                              color: _secondsRemaining < 300
                                  ? const Color(0xFFFFEEEE)
                                  : const Color(0xFFEAF1FF),
                              borderRadius: BorderRadius.circular(30),
                            ),
                            child: Row(
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                Icon(
                                  Icons.schedule_rounded,
                                  size: 16,
                                  color: _secondsRemaining < 300
                                      ? const Color(0xFFC43D4B)
                                      : Theme.of(context).colorScheme.primary,
                                ),
                                const SizedBox(width: 6),
                                Text(
                                  _clock(_secondsRemaining),
                                  style: TextStyle(
                                    color: _secondsRemaining < 300
                                        ? const Color(0xFFC43D4B)
                                        : Theme.of(context).colorScheme.primary,
                                    fontWeight: FontWeight.w800,
                                    fontFeatures: const [
                                      FontFeature.tabularFigures(),
                                    ],
                                  ),
                                ),
                              ],
                            ),
                          ),
                        ],
                      ),
                      const SizedBox(height: 11),
                      ClipRRect(
                        borderRadius: BorderRadius.circular(20),
                        child: LinearProgressIndicator(
                          value: (_current + 1) / _questions.length,
                          minHeight: 6,
                          backgroundColor: const Color(0xFFE9EEF6),
                        ),
                      ),
                    ],
                  ),
                ),
                Expanded(
                  child: ListView(
                    padding: const EdgeInsets.fromLTRB(18, 20, 18, 24),
                    children: [
                      Container(
                        padding: const EdgeInsets.symmetric(
                          horizontal: 11,
                          vertical: 7,
                        ),
                        decoration: BoxDecoration(
                          color: const Color(0xFFEAF1FF),
                          borderRadius: BorderRadius.circular(9),
                        ),
                        child: Text(
                          'PART ${question['part_number'] ?? ''}',
                          style: TextStyle(
                            color: Theme.of(context).colorScheme.primary,
                            fontWeight: FontWeight.w800,
                            fontSize: 12,
                            letterSpacing: 0.6,
                          ),
                        ),
                      ),
                      const SizedBox(height: 14),
                      if (_passageFor(question).isNotEmpty)
                        Padding(
                          padding: const EdgeInsets.only(bottom: 12),
                          child: Card(
                            child: Padding(
                              padding: const EdgeInsets.all(16),
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  const Text(
                                    'ĐOẠN VĂN',
                                    style: TextStyle(
                                      color: Color(0xFF718096),
                                      fontSize: 11,
                                      fontWeight: FontWeight.w800,
                                      letterSpacing: 0.7,
                                    ),
                                  ),
                                  const SizedBox(height: 9),
                                  Text(
                                    _plainText(_passageFor(question)),
                                    style: const TextStyle(height: 1.55),
                                  ),
                                ],
                              ),
                            ),
                          ),
                        ),
                      Card(
                        child: Padding(
                          padding: const EdgeInsets.all(20),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                'CÂU ${question['question_number'] ?? _current + 1}',
                                style: const TextStyle(
                                  color: Color(0xFF718096),
                                  fontSize: 11,
                                  fontWeight: FontWeight.w800,
                                  letterSpacing: 0.7,
                                ),
                              ),
                              const SizedBox(height: 10),
                              Text(
                                _plainText(
                                  question['content_html']?.toString() ?? '',
                                ),
                                style: Theme.of(context).textTheme.titleMedium
                                    ?.copyWith(
                                      fontWeight: FontWeight.w600,
                                      height: 1.55,
                                    ),
                              ),
                            ],
                          ),
                        ),
                      ),
                      const Padding(
                        padding: EdgeInsets.fromLTRB(4, 20, 4, 8),
                        child: Text(
                          'Chọn một đáp án',
                          style: TextStyle(
                            color: Color(0xFF17243A),
                            fontSize: 16,
                            fontWeight: FontWeight.w800,
                          ),
                        ),
                      ),
                      RadioGroup<int>(
                        groupValue: _answers[questionId],
                        onChanged: (value) {
                          if (value != null && !_saving && !_submitting) {
                            _choose(questionId, value);
                          }
                        },
                        child: Column(
                          children: [
                            for (final option in options)
                              Card(
                                margin: const EdgeInsets.only(bottom: 9),
                                clipBehavior: Clip.antiAlias,
                                child: RadioListTile<int>(
                                  value: (option['option_id'] as num).toInt(),
                                  selected:
                                      _answers[questionId] ==
                                      (option['option_id'] as num).toInt(),
                                  activeColor: Theme.of(context)
                                      .colorScheme
                                      .primary,
                                  title: Text(
                                    '${option['label'] ?? ''}. ${_plainText(option['content_html']?.toString() ?? '')}',
                                    style: const TextStyle(
                                      fontWeight: FontWeight.w600,
                                    ),
                                  ),
                                  contentPadding: const EdgeInsets.symmetric(
                                    horizontal: 10,
                                    vertical: 4,
                                  ),
                                ),
                              ),
                          ],
                        ),
                      ),
                      if (_saving)
                        const Padding(
                          padding: EdgeInsets.only(top: 6),
                          child: Text(
                            'Đang lưu câu trả lời…',
                            textAlign: TextAlign.center,
                            style: TextStyle(color: Color(0xFF718096)),
                          ),
                        ),
                    ],
                  ),
                ),
                SafeArea(
                  top: false,
                  child: Padding(
                    padding: const EdgeInsets.fromLTRB(16, 8, 16, 12),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Row(
                          children: [
                            OutlinedButton(
                              onPressed: _saving || _submitting || _current == 0
                                  ? null
                                  : () => setState(() => _current--),
                              child: const Text('Trước'),
                            ),
                            const Spacer(),
                            TextButton(
                              onPressed:
                                  _saving ||
                                      _submitting ||
                                      _current == _questions.length - 1
                                  ? null
                                  : () => setState(() => _current++),
                              child: const Text('Tiếp theo'),
                            ),
                          ],
                        ),
                        const SizedBox(height: 8),
                        SizedBox(
                          width: double.infinity,
                          child: FilledButton.icon(
                            onPressed: _saving || _submitting
                                ? null
                                : () => _submit(),
                            icon: _submitting
                                ? const SizedBox(
                                    width: 18,
                                    height: 18,
                                    child: CircularProgressIndicator(
                                      strokeWidth: 2,
                                    ),
                                  )
                                : const Icon(Icons.check),
                            label: Text(
                              _submitting ? 'Đang nộp bài…' : 'Nộp bài',
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ],
            ),
    );
  }

  String _passageFor(Map<String, dynamic> question) {
    final passageId = question['passage_id'];
    if (passageId == null) return '';
    final parts = widget.exam['parts'];
    if (parts is! List) return '';
    for (final part in parts.whereType<Map<String, dynamic>>()) {
      final passages = part['passages'];
      if (passages is! List) continue;
      for (final passage in passages.whereType<Map<String, dynamic>>()) {
        if (passage['passage_id'].toString() == passageId.toString()) {
          return passage['content_html']?.toString() ?? '';
        }
      }
    }
    return '';
  }
}

class ResultPage extends StatelessWidget {
  const ResultPage({required this.result, super.key});

  final Map<String, dynamic> result;

  @override
  Widget build(BuildContext context) {
    final score = (result['score_percent'] as num?)?.toDouble() ?? 0;
    final answers = result['answers'] is List
        ? (result['answers'] as List).whereType<Map<String, dynamic>>().toList()
        : <Map<String, dynamic>>[];
    return Scaffold(
      appBar: AppBar(title: const Text('Kết quả')),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(18, 22, 18, 36),
        children: [
          Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 900),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Card(
                    child: Padding(
                      padding: const EdgeInsets.all(24),
                      child: Column(
                        children: [
                          Container(
                            width: 68,
                            height: 68,
                            decoration: BoxDecoration(
                              color: const Color(0xFFEAF1FF),
                              borderRadius: BorderRadius.circular(22),
                            ),
                            child: Icon(
                              Icons.emoji_events_rounded,
                              size: 36,
                              color: Theme.of(context).colorScheme.primary,
                            ),
                          ),
                          const SizedBox(height: 14),
                          const Text(
                            'Hoàn thành bài thi!',
                            style: TextStyle(
                              color: Color(0xFF17243A),
                              fontSize: 21,
                              fontWeight: FontWeight.w800,
                            ),
                          ),
                          const SizedBox(height: 6),
                          Text(
                            result['title']?.toString() ?? 'TOEIC Reading',
                            textAlign: TextAlign.center,
                            style: const TextStyle(color: Color(0xFF718096)),
                          ),
                          const SizedBox(height: 18),
                          Text(
                            '${score.toStringAsFixed(0)}%',
                            style: Theme.of(context).textTheme.displayMedium
                                ?.copyWith(
                                  color: Theme.of(context).colorScheme.primary,
                                  fontWeight: FontWeight.w800,
                                ),
                          ),
                          const SizedBox(height: 4),
                          Text(
                            '${result['correct_count'] ?? 0} / ${result['total_questions'] ?? 0} câu đúng',
                            style: const TextStyle(
                              color: Color(0xFF536176),
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                          const SizedBox(height: 20),
                          SizedBox(
                            width: double.infinity,
                            child: FilledButton.icon(
                              onPressed: () =>
                                  Navigator.of(context)
                                      .popUntil((route) => route.isFirst),
                              icon: const Icon(Icons.home_rounded),
                              label: const Text('Về danh sách đề'),
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                  const SizedBox(height: 26),
                  const Text(
                    'Xem lại câu trả lời',
                    style: TextStyle(
                      color: Color(0xFF17243A),
                      fontSize: 20,
                      fontWeight: FontWeight.w800,
                    ),
                  ),
                  const SizedBox(height: 12),
                  for (var index = 0; index < answers.length; index++)
                    _AnswerReviewCard(index: index, answer: answers[index]),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _AnswerReviewCard extends StatelessWidget {
  const _AnswerReviewCard({required this.index, required this.answer});

  final int index;
  final Map<String, dynamic> answer;

  @override
  Widget build(BuildContext context) {
    final isCorrect =
        answer['selected_label'] != null &&
        answer['selected_label'] == answer['correct_label'];
    final accent = isCorrect
        ? const Color(0xFF1A9A68)
        : const Color(0xFFC43D4B);
    return Card(
      margin: const EdgeInsets.only(bottom: 10),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Container(
              width: 34,
              height: 34,
              decoration: BoxDecoration(
                color: accent.withValues(alpha: 0.1),
                borderRadius: BorderRadius.circular(10),
              ),
              child: Center(
                child: Text(
                  '${index + 1}',
                  style: TextStyle(color: accent, fontWeight: FontWeight.w800),
                ),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    _plainText(answer['content_html']?.toString() ?? ''),
                    style: const TextStyle(
                      color: Color(0xFF17243A),
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  const SizedBox(height: 8),
                  Text(
                    'Bạn chọn: ${answer['selected_label'] ?? 'Chưa trả lời'}'
                    '  •  Đáp án đúng: ${answer['correct_label'] ?? '—'}',
                    style: TextStyle(
                      color: accent,
                      fontSize: 13,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ],
              ),
            ),
            Icon(
              isCorrect ? Icons.check_circle_rounded : Icons.cancel_rounded,
              color: accent,
            ),
          ],
        ),
      ),
    );
  }
}

class _BrandTitle extends StatelessWidget {
  const _BrandTitle();

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Container(
          width: 38,
          height: 38,
          decoration: BoxDecoration(
            color: Theme.of(context).colorScheme.primary,
            borderRadius: BorderRadius.circular(11),
          ),
          child: const Icon(
            Icons.menu_book_rounded,
            color: Colors.white,
            size: 22,
          ),
        ),
        const SizedBox(width: 10),
        const Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Text('TOEIC Reading'),
            Text(
              'PRACTICE',
              style: TextStyle(
                color: Color(0xFF8290A5),
                fontSize: 9,
                letterSpacing: 1.2,
                fontWeight: FontWeight.w700,
              ),
            ),
          ],
        ),
      ],
    );
  }
}

class _PracticeBanner extends StatelessWidget {
  const _PracticeBanner({required this.examCount});

  final int examCount;

  @override
  Widget build(BuildContext context) {
    final primary = Theme.of(context).colorScheme.primary;
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(22),
      decoration: BoxDecoration(
        gradient: LinearGradient(
          colors: [primary, const Color(0xFF1748B5)],
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
        ),
        borderRadius: BorderRadius.circular(20),
      ),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text(
                  'Sẵn sàng chinh phục Reading?',
                  style: TextStyle(
                    color: Colors.white,
                    fontSize: 20,
                    fontWeight: FontWeight.w800,
                  ),
                ),
                const SizedBox(height: 7),
                Text(
                  '$examCount đề thi đang chờ bạn luyện tập',
                  style: const TextStyle(
                    color: Color(0xFFDCE7FF),
                    fontSize: 14,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: 12),
          Container(
            width: 54,
            height: 54,
            decoration: BoxDecoration(
              color: Colors.white.withValues(alpha: 0.16),
              borderRadius: BorderRadius.circular(16),
            ),
            child: const Icon(
              Icons.auto_stories_rounded,
              color: Colors.white,
              size: 28,
            ),
          ),
        ],
      ),
    );
  }
}

class _CountBadge extends StatelessWidget {
  const _CountBadge({required this.count});

  final int count;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
      decoration: BoxDecoration(
        color: const Color(0xFFEAF1FF),
        borderRadius: BorderRadius.circular(30),
      ),
      child: Text(
        '$count đề',
        style: TextStyle(
          color: Theme.of(context).colorScheme.primary,
          fontSize: 13,
          fontWeight: FontWeight.w700,
        ),
      ),
    );
  }
}

class _ExamCard extends StatelessWidget {
  const _ExamCard({required this.exam, required this.onTap});

  final Map<String, dynamic> exam;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(18),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Container(
                    width: 48,
                    height: 48,
                    decoration: BoxDecoration(
                      color: const Color(0xFFEAF1FF),
                      borderRadius: BorderRadius.circular(14),
                    ),
                    child: Icon(
                      Icons.description_outlined,
                      color: Theme.of(context).colorScheme.primary,
                    ),
                  ),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Text(
                      exam['title']?.toString() ?? 'Đề TOEIC Reading',
                      style: const TextStyle(
                        color: Color(0xFF17243A),
                        fontSize: 16,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                  ),
                  Icon(
                    Icons.arrow_forward_rounded,
                    color: Theme.of(context).colorScheme.primary,
                  ),
                ],
              ),
              const SizedBox(height: 16),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  _ExamInfoChip(
                    icon: Icons.help_outline_rounded,
                    text: '${exam['question_count'] ?? 0} câu hỏi',
                  ),
                  _ExamInfoChip(
                    icon: Icons.schedule_rounded,
                    text: '${exam['duration_minutes'] ?? 0} phút',
                  ),
                  _ExamInfoChip(
                    icon: Icons.menu_book_outlined,
                    text: exam['parts']?.toString() ?? 'Reading',
                  ),
                ],
              ),
              const SizedBox(height: 16),
              const Divider(height: 1, color: Color(0xFFEDF0F5)),
              const SizedBox(height: 12),
              Row(
                children: [
                  Text(
                    'Bắt đầu luyện tập',
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.primary,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  const SizedBox(width: 5),
                  Icon(
                    Icons.north_east_rounded,
                    size: 17,
                    color: Theme.of(context).colorScheme.primary,
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _ExamInfoChip extends StatelessWidget {
  const _ExamInfoChip({required this.icon, required this.text});

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 7),
      decoration: BoxDecoration(
        color: const Color(0xFFF4F6FA),
        borderRadius: BorderRadius.circular(9),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 15, color: const Color(0xFF718096)),
          const SizedBox(width: 6),
          Text(
            text,
            style: const TextStyle(
              color: Color(0xFF536176),
              fontSize: 12,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
      ),
    );
  }
}

class _ExamIntroduction extends StatelessWidget {
  const _ExamIntroduction({
    required this.exam,
    required this.questionCount,
    required this.onStart,
  });

  final Map<String, dynamic> exam;
  final int questionCount;
  final VoidCallback onStart;

  @override
  Widget build(BuildContext context) {
    final primary = Theme.of(context).colorScheme.primary;
    return Center(
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(20),
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 560),
          child: Card(
            child: Padding(
              padding: const EdgeInsets.all(26),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Container(
                    width: 74,
                    height: 74,
                    decoration: BoxDecoration(
                      color: const Color(0xFFEAF1FF),
                      borderRadius: BorderRadius.circular(22),
                    ),
                    child: Icon(
                      Icons.menu_book_rounded,
                      color: primary,
                      size: 36,
                    ),
                  ),
                  const SizedBox(height: 20),
                  Text(
                    'Bài luyện Reading',
                    style: TextStyle(
                      color: primary,
                      fontSize: 13,
                      fontWeight: FontWeight.w800,
                      letterSpacing: 0.5,
                    ),
                  ),
                  const SizedBox(height: 8),
                  Text(
                    exam['title']?.toString() ?? 'TOEIC Reading',
                    textAlign: TextAlign.center,
                    style: Theme.of(context).textTheme.headlineSmall
                        ?.copyWith(fontWeight: FontWeight.w800),
                  ),
                  const SizedBox(height: 20),
                  Wrap(
                    spacing: 9,
                    runSpacing: 9,
                    alignment: WrapAlignment.center,
                    children: [
                      _ExamInfoChip(
                        icon: Icons.help_outline_rounded,
                        text: '$questionCount câu hỏi',
                      ),
                      _ExamInfoChip(
                        icon: Icons.schedule_rounded,
                        text: '${exam['duration_minutes']} phút',
                      ),
                    ],
                  ),
                  const SizedBox(height: 22),
                  const Divider(height: 1, color: Color(0xFFEDF0F5)),
                  const SizedBox(height: 18),
                  const Row(
                    children: [
                      Icon(
                        Icons.info_outline_rounded,
                        color: Color(0xFF718096),
                        size: 20,
                      ),
                      SizedBox(width: 9),
                      Expanded(
                        child: Text(
                          'Câu trả lời được lưu tự động trong quá trình làm bài.',
                          style: TextStyle(color: Color(0xFF536176)),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 22),
                  SizedBox(
                    width: double.infinity,
                    child: FilledButton.icon(
                      onPressed: onStart,
                      icon: const Icon(Icons.play_arrow_rounded),
                      label: const Text('Bắt đầu làm bài'),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _MessageView extends StatelessWidget {
  const _MessageView({required this.message, this.onRetry});

  final String message;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.all(24),
      children: [
        const SizedBox(height: 100),
        const Icon(Icons.menu_book_outlined, size: 46, color: Colors.black45),
        const SizedBox(height: 16),
        Text(message, textAlign: TextAlign.center),
        if (onRetry != null) ...[
          const SizedBox(height: 16),
          Center(
            child: OutlinedButton(
              onPressed: onRetry,
              child: const Text('Thử lại'),
            ),
          ),
        ],
      ],
    );
  }
}

String _plainText(String value) {
  return value
      .replaceAll(RegExp(r'<[^>]*>'), ' ')
      .replaceAll('&nbsp;', ' ')
      .replaceAll('&amp;', '&')
      .replaceAll('&lt;', '<')
      .replaceAll('&gt;', '>')
      .replaceAll('&quot;', '"')
      .replaceAll('&#39;', "'")
      .replaceAll(RegExp(r'\s+'), ' ')
      .trim();
}

String _clock(int seconds) {
  final minutes = seconds ~/ 60;
  final remainingSeconds = seconds % 60;
  return '${minutes.toString().padLeft(2, '0')}:'
      '${remainingSeconds.toString().padLeft(2, '0')}';
}

void _showError(BuildContext context, Object error) {
  ScaffoldMessenger.of(context).showSnackBar(
    SnackBar(content: Text(error.toString().replaceFirst('Exception: ', ''))),
  );
}
