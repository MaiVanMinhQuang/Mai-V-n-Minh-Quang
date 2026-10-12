const root = document.getElementById("app");
const state = {
  token: localStorage.getItem("toeic-token") || sessionStorage.getItem("toeic-token"),
  user: JSON.parse(localStorage.getItem("toeic-user") || sessionStorage.getItem("toeic-user") || "null"),
  screen: "home",
  exams: [],
  currentExam: null,
  currentAttempt: null,
  answers: {},
  activeQuestion: 0,
  activePart: null,
  editExam: null,
  editQuestion: null,
  importFile: null,
  timerInterval: null
};

const api = async (path, options = {}) => {
  const headers = new Headers(options.headers || {});
  if (state.token) headers.set("Authorization", `Bearer ${state.token}`);
  if (options.body && !(options.body instanceof FormData)) headers.set("Content-Type", "application/json");
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 15000);
  let response;
  try {
    response = await fetch(`/api${path}`, {
      ...options,
      headers,
      signal: controller.signal
    });
  } catch (error) {
    if (error.name === "AbortError") {
      throw new Error("Máy chủ phản hồi quá lâu. Hãy kiểm tra Backend và SQL Server rồi thử lại.");
    }
    throw error;
  } finally {
    clearTimeout(timeout);
  }
  const type = response.headers.get("content-type") || "";
  const payload = type.includes("application/json") ? await response.json() : await response.text();
  if (!response.ok) {
    throw new Error(payload?.message || payload?.error?.message || payload?.result?.message ||
      payload?.error?.details || `Lỗi máy chủ (${response.status})`);
  }
  if (payload && Object.prototype.hasOwnProperty.call(payload, "result")) return payload.result;
  return payload;
};

const esc = value => String(value ?? "").replace(/[&<>"']/g, char => ({
  "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
}[char]));
const isAdmin = () => state.user?.role === "admin";
const formatDate = value => value ? new Date(value).toLocaleDateString("vi-VN") : "—";
const toast = (message, error = false) => {
  document.querySelector(".toast")?.remove();
  const element = document.createElement("div");
  element.className = `toast${error ? " error" : ""}`;
  element.textContent = message;
  document.body.append(element);
  setTimeout(() => element.remove(), 3600);
};

function renderLoading(message) {
  root.innerHTML = `<main class="login-page"><section class="login-card loading-card">
    ${brand()}
    <div class="loading-indicator" role="status" aria-live="polite">
      <span class="spinner" aria-hidden="true"></span>
      <span>${esc(message)}</span>
    </div>
  </section></main>`;
}

function brand() {
  return `<div class="brand"><span class="brand-mark">T</span><span class="brand-copy">TOEIC Reading<small>Practice</small></span></div>`;
}

function renderLogin() {
  root.innerHTML = `<main class="login-page"><section class="login-card">
    ${brand()}
    <p class="eyebrow">Chào mừng trở lại</p>
    <h1>Đăng nhập tài khoản</h1>
    <p>Tiếp tục hành trình chinh phục mục tiêu TOEIC của bạn.</p>
    <form id="login-form">
      <div class="field"><label for="email">Email</label><input id="email" name="email" type="email" autocomplete="username" placeholder="sinhvien@university.edu.vn" required></div>
      <div class="field"><label for="password">Mật khẩu</label><input id="password" name="password" type="password" autocomplete="current-password" placeholder="Nhập mật khẩu" required></div>
      <div class="form-row"><label><input type="checkbox" name="remember"> Ghi nhớ đăng nhập</label><span>An toàn & bảo mật</span></div>
      <p class="login-error hidden" id="login-error"></p>
      <button class="button button-primary button-block" type="submit">Đăng nhập <span aria-hidden="true">→</span></button>
    </form>
    <div class="login-hint">Khu vực quản trị viên. Ứng dụng luyện thi dành cho học viên không yêu cầu đăng nhập.</div>
  </section></main>`;
  document.getElementById("login-form").addEventListener("submit", login);
}

async function login(event) {
  event.preventDefault();
  const form = new FormData(event.currentTarget);
  const button = event.currentTarget.querySelector("button");
  button.disabled = true;
  button.textContent = "Đang đăng nhập…";
  try {
    const result = await api("/auth/login", {
      method: "POST",
      body: JSON.stringify({ email: form.get("email"), password: form.get("password") })
    });
    if (result.user?.role !== "admin") throw new Error("Chỉ tài khoản quản trị được đăng nhập tại đây.");
    state.token = result.accessToken;
    state.user = result.user;
    const store = form.get("remember") ? localStorage : sessionStorage;
    const other = form.get("remember") ? sessionStorage : localStorage;
    store.setItem("toeic-token", state.token);
    store.setItem("toeic-user", JSON.stringify(state.user));
    other.removeItem("toeic-token");
    other.removeItem("toeic-user");
    state.screen = "home";
    await render();
  } catch (error) {
    const message = document.getElementById("login-error");
    message.textContent = error.message;
    message.classList.remove("hidden");
    button.disabled = false;
    button.innerHTML = "Đăng nhập <span aria-hidden=\"true\">→</span>";
  }
}

function shell(content) {
  const name = esc(state.user?.fullName || "Người dùng");
  const initial = esc(name.trim().split(/\s+/).slice(-1)[0]?.slice(0, 2).toUpperCase() || "TO");
  return `<header class="topbar">${brand()}<div class="top-right"><div class="avatar">${initial}</div><div class="account-copy"><strong>${name}</strong><small>${isAdmin() ? "Quản trị viên" : "Học viên"}</small></div><button class="button button-outline" data-action="logout" aria-label="Đăng xuất">Thoát</button></div></header>
  <div class="layout"><nav class="sidebar"><div class="side-label">${isAdmin() ? "Quản trị" : "Học tập"}</div>
    <button class="nav-button ${state.screen === "home" ? "active" : ""}" data-action="home"><span class="nav-icon">⌂</span>${isAdmin() ? "Tổng quan" : "Đề thi của tôi"}</button>
    ${isAdmin() ? `<button class="nav-button ${state.screen === "manage" ? "active" : ""}" data-action="manage"><span class="nav-icon">▤</span>Quản lý đề thi</button>
    <button class="nav-button ${state.screen === "import" ? "active" : ""}" data-action="import"><span class="nav-icon">⇧</span>Import đề thi</button>` : ""}
  </nav><main class="main">${content}</main></div>`;
}

function pageHeading(title, detail, action = "") {
  return `<div class="page-heading"><div><h1>${title}</h1><p>${detail}</p></div>${action}</div>`;
}

async function loadExams(admin = isAdmin()) {
  state.exams = await api(admin ? "/admin/exams" : "/exams");
}

function stats(exams) {
  const published = exams.filter(exam => exam.status === "published").length;
  const drafts = exams.filter(exam => exam.status === "draft").length;
  return `<div class="stats"><div class="stat-card"><div><small>Tổng số đề</small><strong>${exams.length}</strong></div><span class="stat-icon">▤</span></div>
    <div class="stat-card"><div><small>Đã xuất bản</small><strong>${published}</strong></div><span class="stat-icon">✓</span></div>
    <div class="stat-card"><div><small>Bản nháp</small><strong>${drafts}</strong></div><span class="stat-icon">◷</span></div></div>`;
}

function examTable(exams) {
  if (!exams.length) return `<div class="empty"><div class="empty-icon">▤</div><h3>Chưa có đề thi</h3><p>${isAdmin() ? "Import đề TOEIC dạng Word (.docx) để bắt đầu quản lý nội dung." : "Hiện chưa có đề thi được xuất bản. Hãy quay lại sau nhé."}</p>${isAdmin() ? `<button class="button button-primary" data-action="import">Import đề thi</button>` : ""}</div>`;
  return `<div class="table-wrap"><table><thead><tr><th>Tên đề</th><th>Số câu</th><th>Thời lượng</th><th>Trạng thái</th><th>Cập nhật</th><th>Hành động</th></tr></thead><tbody>
    ${exams.map(exam => `<tr><td class="table-title"><strong>${esc(exam.title)}</strong><span>${esc(exam.parts || "Part 5 • Part 6 • Part 7")}</span></td>
    <td>${exam.question_count || 0} câu</td><td>${exam.duration_minutes} phút</td>
    <td><span class="badge ${exam.status === "published" ? "published" : ""}">${exam.status === "published" ? "Đã xuất bản" : "Bản nháp"}</span></td>
    <td>${formatDate(exam.created_at)}</td><td><div class="row-actions">
      ${isAdmin() ? `<button class="button button-secondary" data-action="edit" data-id="${exam.exam_id}">Chỉnh sửa</button>${exam.status === "draft" ? `<button class="button button-outline" data-action="publish" data-id="${exam.exam_id}">Xuất bản</button>` : ""}<button class="button button-danger" data-action="delete" data-status="${exam.status}" data-id="${exam.exam_id}" aria-label="Xóa">Xóa</button>` : `<button class="button button-primary" data-action="start" data-id="${exam.exam_id}">Làm bài</button>`}
    </div></td></tr>`).join("")}
  </tbody></table></div>`;
}

async function renderHome() {
  await loadExams();
  const content = isAdmin()
    ? `${pageHeading("Quản lý đề thi", "Tạo mới, chỉnh sửa và quản lý trạng thái đề thi.", `<button class="button button-primary" data-action="import">＋ Import đề thi</button>`)}
      ${stats(state.exams)}<section class="panel"><div class="toolbar"><input class="search-input" id="exam-search" placeholder="Tìm kiếm đề thi…"><select id="status-filter" aria-label="Lọc trạng thái"><option value="">Tất cả trạng thái</option><option value="published">Đã xuất bản</option><option value="draft">Bản nháp</option></select></div><div id="exam-table">${examTable(state.exams)}</div></section>`
    : `${pageHeading("Chọn đề thi", "Chọn một bài Reading để bắt đầu luyện tập.")}${state.exams.length
      ? `<section class="panel"><div class="toolbar"><span class="muted small">${state.exams.length} đề đã sẵn sàng</span><span class="small muted">Reading • Parts 5, 6 &amp; 7</span></div>${examTable(state.exams)}</section>`
      : `<section class="panel">${examTable(state.exams)}</section>`}`;
  root.innerHTML = shell(content);
  bindCommon();
  document.getElementById("exam-search")?.addEventListener("input", filterExams);
  document.getElementById("status-filter")?.addEventListener("change", filterExams);
}

function filterExams() {
  const query = document.getElementById("exam-search")?.value.toLowerCase() || "";
  const status = document.getElementById("status-filter")?.value || "";
  const filtered = state.exams.filter(exam => exam.title.toLowerCase().includes(query) && (!status || exam.status === status));
  document.getElementById("exam-table").innerHTML = examTable(filtered);
}

function renderImport() {
  root.innerHTML = shell(`${pageHeading("Import đề thi từ Word", "Tải lên .docx theo mẫu; bọc lựa chọn đúng trong &lt;ANSWER&gt;&lt;OPTION label=\"C\"&gt;...&lt;/OPTION&gt;&lt;/ANSWER&gt;. Cũng hỗ trợ dạng &lt;ANSWER&gt;C&lt;/ANSWER&gt;.")}<section class="panel import-panel">
    <label class="dropzone" id="dropzone" for="file-input"><span class="empty-icon">⇧</span><strong>Kéo thả file Word vào đây</strong><p>hoặc chọn file từ thiết bị của bạn</p><span class="button button-outline">Chọn file</span></label>
    <input id="file-input" type="file" accept=".docx,application/vnd.openxmlformats-officedocument.wordprocessingml.document" hidden>
    <div class="file-meta"><span>Hỗ trợ: .docx • Tối đa 20 MB</span><span id="file-name">Chưa chọn file</span></div>
    <div class="upload-actions"><button class="button button-primary" id="import-submit" disabled>Import đề</button></div>
  </section>`);
  bindCommon();
  const input = document.getElementById("file-input");
  input.addEventListener("change", () => selectFile(input.files[0]));
  const zone = document.getElementById("dropzone");
  zone.addEventListener("dragover", event => { event.preventDefault(); zone.classList.add("dragover"); });
  zone.addEventListener("dragleave", () => zone.classList.remove("dragover"));
  zone.addEventListener("drop", event => {
    event.preventDefault();
    zone.classList.remove("dragover");
    selectFile(event.dataTransfer.files[0]);
  });
  document.getElementById("import-submit").addEventListener("click", importExam);
}

function selectFile(file) {
  if (!file) return;
  if (!file.name.toLowerCase().endsWith(".docx") || file.size > 20 * 1024 * 1024) {
    toast("Chỉ hỗ trợ file .docx tối đa 20 MB.", true);
    return;
  }
  state.importFile = file;
  document.getElementById("file-name").textContent = `${file.name} • ${(file.size / 1024 / 1024).toFixed(1)} MB`;
  document.getElementById("import-submit").disabled = false;
}

async function importExam() {
  if (!state.importFile) return;
  const button = document.getElementById("import-submit");
  button.disabled = true;
  button.textContent = "Đang xử lý…";
  try {
    const form = new FormData();
    form.append("file", state.importFile);
    const result = await api("/admin/import", { method: "POST", body: form });
    toast(`Đã import ${result.questionCount} câu, nhận ${result.keyedQuestionCount} đáp án từ file.`);
    state.importFile = null;
    await openEditor(result.examId);
  } catch (error) {
    toast(error.message, true);
    button.disabled = false;
    button.textContent = "Import đề";
  }
}

async function openEditor(examId) {
  state.editExam = await api(`/exams/${examId}`);
  state.activePart = state.editExam.parts[0]?.part_id;
  state.editQuestion = state.editExam.parts[0]?.questions[0] || null;
  state.screen = "editor";
  renderEditor();
}

function renderEditor() {
  const exam = state.editExam;
  if (!exam) return renderHome();
  const part = exam.parts.find(item => item.part_id === state.activePart) || exam.parts[0];
  if (!part) return renderHome();
  const question = part.questions.find(item => item.question_id === state.editQuestion?.question_id) || part.questions[0];
  state.editQuestion = question;
  const allQuestions = part.questions;
  const keyed = exam.parts.reduce((total, item) => total + item.questions.filter(q => q.correct_option_id).length, 0);
  const total = exam.parts.reduce((count, item) => count + item.questions.length, 0);
  root.innerHTML = shell(`<div class="editor-head"><div><h1>${esc(exam.title)}</h1><span class="badge">${exam.status === "published" ? "Đã xuất bản" : "Bản nháp"}</span><p class="muted small" style="margin:8px 0 0">Đáp án đã bổ sung: ${keyed} / ${total} • ${exam.duration_minutes} phút</p></div>
    <div class="editor-actions"><button class="button button-outline" data-action="back-manage">Quay lại</button>${exam.status === "draft" ? `<button class="button button-primary" data-action="publish" data-id="${exam.exam_id}">Xuất bản</button>` : ""}</div></div>
    <div class="editor"><aside class="panel editor-nav"><div class="tabs">${exam.parts.map(item => `<button class="tab ${item.part_id === part.part_id ? "active" : ""}" data-part="${item.part_id}">Part ${item.part_number}</button>`).join("")}</div>
      <div class="question-list">${allQuestions.map(item => `<button class="question-select ${item.question_id === question?.question_id ? "active" : ""}" data-question="${item.question_id}"><strong>Câu ${item.question_number}</strong><small>${item.correct_option_id ? "✓ Đã có đáp án" : "Chưa có đáp án"}</small></button>`).join("")}</div>
    </aside>
    <section class="panel editor-form">${question ? editorQuestion(question, part) : `<div class="empty"><h3>Phần thi chưa có câu hỏi</h3></div>`}</section></div>`);
  bindCommon();
  document.querySelectorAll("[data-part]").forEach(button => button.addEventListener("click", () => {
    state.activePart = Number(button.dataset.part);
    state.editQuestion = state.editExam.parts.find(item => item.part_id === state.activePart)?.questions[0] || null;
    renderEditor();
  }));
  document.querySelectorAll("[data-question]").forEach(button => button.addEventListener("click", () => {
    state.editQuestion = part.questions.find(item => item.question_id === Number(button.dataset.question));
    renderEditor();
  }));
  document.getElementById("save-question")?.addEventListener("click", saveQuestion);
}

function editorQuestion(question, part) {
  const passage = part.passages.find(item => item.passage_id === question.passage_id);
  return `<h2>Chỉnh sửa câu ${question.question_number}</h2>${passage ? `<div class="editor-warning">Bài đọc: ${esc(passage.passage_number || passage.passage_type || "")}<br>${esc(passage.content_html.slice(0, 170))}…</div>` : ""}
    ${!question.correct_option_id ? `<div class="editor-warning">Chưa có đáp án đúng. Chọn nút tròn để đặt đáp án trước khi lưu hoặc xuất bản.</div>` : ""}
    <div class="field"><label for="question-content">Nội dung câu hỏi</label><textarea id="question-content">${esc(question.content_html)}</textarea></div>
    <div class="field"><label>Các lựa chọn <span class="muted">(chọn đáp án đúng)</span></label></div>
    ${(question.options || []).map(option => `<label class="editor-option"><input type="radio" name="correct-option" value="${option.option_id}" ${question.correct_option_id === option.option_id ? "checked" : ""}><span class="option-label">${esc(option.label)}</span><input type="text" data-option-id="${option.option_id}" value="${esc(option.content_html)}" aria-label="Lựa chọn ${esc(option.label)}"></label>`).join("")}
    <div class="editor-footer"><button class="button button-primary" id="save-question">Lưu câu hỏi</button></div>`;
}

async function saveQuestion() {
  const question = state.editQuestion;
  const part = state.editExam.parts.find(item => item.part_id === state.activePart);
  const selected = document.querySelector('input[name="correct-option"]:checked');
  const options = [...document.querySelectorAll("[data-option-id]")].map(input => ({
    label: question.options.find(option => option.option_id === Number(input.dataset.optionId)).label,
    content: input.value
  }));
  try {
    await api(`/admin/questions/${question.question_id}`, {
      method: "PUT",
      body: JSON.stringify({
        content: document.getElementById("question-content").value,
        correctLabel: selected ? question.options.find(option => option.option_id === Number(selected.value)).label : null,
        options
      })
    });
    toast("Đã lưu nội dung câu hỏi.");
    await openEditor(state.editExam.exam_id);
    state.activePart = part.part_id;
    state.editQuestion = state.editExam.parts.find(item => item.part_id === part.part_id)?.questions.find(item => item.question_number === question.question_number);
    renderEditor();
  } catch (error) {
    toast(error.message, true);
  }
}

function bindCommon() {
  document.querySelectorAll("[data-action]").forEach(button => button.addEventListener("click", () => handleAction(button.dataset.action, button.dataset.id, button.dataset.status)));
}

async function handleAction(action, id, status) {
  try {
    if (action === "logout") {
      clearInterval(state.timerInterval);
      state.token = null;
      state.user = null;
      localStorage.removeItem("toeic-token");
      localStorage.removeItem("toeic-user");
      sessionStorage.removeItem("toeic-token");
      sessionStorage.removeItem("toeic-user");
      renderLogin();
    } else if (action === "home") {
      state.screen = "home";
      await renderHome();
    } else if (action === "manage") {
      state.screen = "manage";
      await renderHome();
    } else if (action === "import") {
      state.screen = "import";
      renderImport();
    } else if (action === "edit") {
      await openEditor(Number(id));
    } else if (action === "back-manage") {
      state.screen = "home";
      await renderHome();
    } else if (action === "publish") {
      await api(`/admin/exams/${id}/publish`, { method: "PUT" });
      toast("Đề thi đã được xuất bản.");
      state.screen = "home";
      await renderHome();
    } else if (action === "delete") {
      const examStatus = status || state.exams.find(exam => String(exam.exam_id) === String(id))?.status;
      const confirmation = examStatus === "published"
        ? "Xóa đề đã xuất bản và toàn bộ lượt làm bài, câu trả lời, kết quả của đề này? Hành động không thể hoàn tác."
        : "Xóa đề nháp này và toàn bộ dữ liệu liên quan? Hành động không thể hoàn tác.";
      if (confirm(confirmation)) {
        await api(`/admin/exams/${id}`, { method: "DELETE" });
        toast("Đã xóa đề thi.");
        await renderHome();
      }
    } else if (action === "start") {
      await startExam(Number(id));
    }
  } catch (error) {
    toast(error.message, true);
  }
}

async function startExam(examId) {
  state.currentExam = await api(`/exams/${examId}`);
  const attempt = await api(`/attempts/${examId}`, { method: "POST" });
  state.currentAttempt = attempt;
  state.answers = {};
  state.activeQuestion = 0;
  state.screen = "taking";
  renderTaking();
}

function flatQuestions() {
  return state.currentExam.parts.flatMap(part => part.questions.map(question => ({ ...question, part })));
}

function renderTaking() {
  const questions = flatQuestions();
  const question = questions[state.activeQuestion];
  if (!question) return renderHome();
  const passage = question.passage_id
    ? question.part.passages.find(item => item.passage_id === question.passage_id)
    : null;
  const answeredCount = Object.keys(state.answers).length;
  const remaining = Math.max(0, state.currentExam.duration_minutes * 60 - Math.floor((Date.now() - new Date(state.currentAttempt.startedAt).getTime()) / 1000));
  root.innerHTML = `<header class="topbar">${brand()}<div class="top-right"><div class="timer ${remaining < 300 ? "urgent" : ""}"><span>◷</span><span id="timer-text">${clock(remaining)}</span></div><button class="button button-danger" data-action="submit-exam">Nộp bài</button></div></header>
  <main class="exam-workspace"><div class="exam-toolbar"><div class="exam-title"><h1>${esc(state.currentExam.title)}</h1><span class="muted small">Part ${question.part.part_number} • Câu ${question.question_number} / ${questions.length}</span></div><span class="muted small">Đã trả lời ${answeredCount} / ${questions.length}</span></div>
    <div class="exam-layout"><div><div class="exam-content">
      <section class="panel passage-box">${passage ? `<h2>${esc(passage.passage_type || "Bài đọc")} ${passage.passage_number ? `· ${esc(passage.passage_number)}` : ""}</h2>${esc(passage.content_html)}` : `<h2>Part ${question.part.part_number}</h2>${esc(question.part.directions || "Chọn đáp án đúng nhất.")}`}</section>
      <section class="panel question-box"><h2>Câu ${question.question_number}</h2><p class="question-text">${esc(question.content_html)}</p>
        ${(question.options || []).map(option => `<label class="answer-choice ${state.answers[question.question_id] === option.option_id ? "selected" : ""}"><input type="radio" name="answer" value="${option.option_id}" ${state.answers[question.question_id] === option.option_id ? "checked" : ""}><strong>${esc(option.label)}.</strong><span>${esc(option.content_html)}</span></label>`).join("")}
      </section></div>
      <div class="exam-bottom"><button class="button button-outline" data-action="previous" ${state.activeQuestion === 0 ? "disabled" : ""}>← Câu trước</button><button class="button button-primary" data-action="next" ${state.activeQuestion === questions.length - 1 ? "disabled" : ""}>Câu tiếp theo →</button></div>
    </div><aside class="panel question-nav"><h3>Danh sách câu hỏi</h3><p>${answeredCount} câu đã trả lời</p><div class="question-grid">${questions.map((item, index) => `<button class="q-number ${index === state.activeQuestion ? "current" : ""} ${state.answers[item.question_id] ? "answered" : ""}" data-jump="${index}" aria-label="Câu ${item.question_number}">${item.question_number}</button>`).join("")}</div><div class="exam-nav-actions"><button class="button button-outline" data-action="save-progress">Lưu tiến độ</button></div></aside></div>
  </main>`;
  document.querySelectorAll('input[name="answer"]').forEach(input => input.addEventListener("change", () => saveAnswer(question, Number(input.value))));
  document.querySelectorAll("[data-jump]").forEach(button => button.addEventListener("click", () => {
    state.activeQuestion = Number(button.dataset.jump);
    renderTaking();
  }));
  document.querySelector('[data-action="previous"]')?.addEventListener("click", () => moveQuestion(-1));
  document.querySelector('[data-action="next"]')?.addEventListener("click", () => moveQuestion(1));
  document.querySelector('[data-action="submit-exam"]')?.addEventListener("click", submitExam);
  document.querySelector('[data-action="save-progress"]')?.addEventListener("click", () => toast("Tiến độ được lưu tự động."));
  clearInterval(state.timerInterval);
  state.timerInterval = setInterval(() => {
    const seconds = Math.max(0, state.currentExam.duration_minutes * 60 - Math.floor((Date.now() - new Date(state.currentAttempt.startedAt).getTime()) / 1000));
    const element = document.getElementById("timer-text");
    if (element) element.textContent = clock(seconds);
    if (seconds === 0) submitExam(true);
  }, 1000);
}

function clock(seconds) {
  return `${String(Math.floor(seconds / 60)).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`;
}

async function saveAnswer(question, optionId) {
  state.answers[question.question_id] = optionId;
  renderTaking();
  try {
    await api(`/attempts/${state.currentAttempt.attemptId}/answers`, {
      method: "PUT",
      body: JSON.stringify({ questionId: question.question_id, selectedOptionId: optionId })
    });
  } catch (error) {
    toast(error.message, true);
  }
}

function moveQuestion(delta) {
  state.activeQuestion = Math.min(flatQuestions().length - 1, Math.max(0, state.activeQuestion + delta));
  renderTaking();
}

async function submitExam(expired = false) {
  if (!expired && !confirm("Nộp bài thi ngay bây giờ?")) return;
  clearInterval(state.timerInterval);
  try {
    const result = await api(`/attempts/${state.currentAttempt.attemptId}/submit`, { method: "POST" });
    state.screen = "result";
    renderResult(result);
  } catch (error) {
    toast(error.message, true);
    renderTaking();
  }
}

function renderResult(result) {
  const score = Number(result.score_percent || 0);
  root.innerHTML = shell(`<section class="panel result-card"><p class="eyebrow">Hoàn thành bài thi</p><h1>${esc(result.title)}</h1><div class="score-circle">${score.toFixed(0)}%</div>
    <p class="muted">${result.correct_count} / ${result.total_questions} câu đúng</p><p class="muted small">Thời gian nộp: ${formatDate(result.submitted_at)}</p><div style="display:flex;justify-content:center;gap:9px;margin:20px 0"><button class="button button-primary" data-action="home">Về danh sách đề</button></div>
    <div class="review-list"><h2 style="font-size:16px">Xem lại đáp án</h2>${(result.answers || []).map(answer => `<div class="review-item"><strong>Câu ${answer.question_number}.</strong> ${esc(answer.content_html)}<br>Bạn chọn: ${answer.selected_label || "Chưa trả lời"} · Đáp án: ${answer.correct_label || "—"} <span class="${answer.selected_label === answer.correct_label ? "correct" : "incorrect"}">${answer.selected_label === answer.correct_label ? "Đúng" : "Sai"}</span></div>`).join("")}</div>
  </section>`);
  bindCommon();
}

async function render() {
  if (!state.token || !state.user) return renderLogin();
  renderLoading("Đang kiểm tra phiên đăng nhập…");
  try {
    const me = await api("/auth/me");
    if (me.role !== "admin") {
      localStorage.removeItem("toeic-token");
      localStorage.removeItem("toeic-user");
      sessionStorage.removeItem("toeic-token");
      sessionStorage.removeItem("toeic-user");
      state.token = null;
      state.user = null;
      return renderLogin();
    }
    state.user = me;
    if (state.screen === "taking") return renderTaking();
    if (state.screen === "result") return;
    if (state.screen === "import") return renderImport();
    if (state.screen === "editor" && state.editExam) return renderEditor();
    return renderHome();
  } catch (error) {
    if (error.message.includes("(401)") || error.message.includes("(403)")) {
      localStorage.removeItem("toeic-token");
      sessionStorage.removeItem("toeic-token");
      state.token = null;
      state.user = null;
      return renderLogin();
    }
    root.innerHTML = `<main class="login-page"><section class="login-card">${brand()}<h1>Không kết nối được</h1><p>${esc(error.message)}</p><button class="button button-primary" onclick="location.reload()">Thử lại</button></section></main>`;
  }
}

render();
