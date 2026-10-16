'use strict';
const $ = id => document.getElementById(id);
const labels = {AwaitingManager: 'Awaiting Manager', AwaitingOwner: 'Awaiting Owner', Approved: 'Approved', Rejected: 'Rejected'};
const badgeClass = {AwaitingManager: 'pending', AwaitingOwner: 'owner', Approved: 'approved', Rejected: 'rejected'};
let currentUser = 'alice', scope = 'mine', users = [], activeRequest = null, listGeneration = 0, detailGeneration = 0, mutating = false;

// Render user-supplied content only through textContent, never HTML interpolation.
function el(tag, text, className) { const node = document.createElement(tag); if (text !== undefined) node.textContent = text; if (className) node.className = className; return node; }
function badge(status) { return el('span', labels[status] || status, 'badge ' + (badgeClass[status] || '')); }
function date(value) { return new Date(value).toLocaleString('id-ID', {dateStyle: 'medium', timeStyle: 'short'}); }
function notify(message, error = false) { $('flash').textContent = message; $('flash').className = error ? 'error' : ''; $('flash').hidden = false; }
function inlineError(id, message) { $(id).textContent = message || ''; $(id).hidden = !message; }
async function api(path, options = {}, actor = currentUser) {
  let response;
  try { response = await fetch('/api' + path, {...options, headers: {'X-User-Id': actor, ...(options.body ? {'Content-Type': 'application/json'} : {})}}); }
  catch (_) { throw Object.assign(new Error('Koneksi ke server terputus. Jika sedang menyimpan, hasilnya belum terkonfirmasi; coba ulang dengan isian yang sama.'), {uncertain: true}); }
  let body;
  try { body = await response.json(); }
  catch (_) { throw Object.assign(new Error('Respons server tidak dapat dibaca. Silakan muat ulang atau retry.'), {uncertain: true}); }
  if (!response.ok) throw Object.assign(new Error(body.error?.message || 'Permintaan gagal.'), {status: response.status, code: body.error?.code, trace: body.request_id, uncertain: response.status >= 500});
  return body;
}
function describeError(error) { return error.message + (error.trace ? ` [Ref: ${error.trace.slice(0, 8)}]` : ''); }
function busy(value) { mutating = value; $('user-select').disabled = value; for (const id of ['submit-request', 'approve-button', 'reject-button', 'reload-detail']) $(id).disabled = value; }
function user() { return users.find(item => item.id === currentUser); }
function draftKey() { return 'accesshub-pending-' + currentUser; }
function readPending() { try { return JSON.parse(sessionStorage.getItem(draftKey()) || 'null'); } catch (_) { return null; } }
function setPending(value) { try { value ? sessionStorage.setItem(draftKey(), JSON.stringify(value)) : sessionStorage.removeItem(draftKey()); } catch (_) { /* Memory copy still protects in-page retries. */ } }
let memoryPending = null;

function riskPreview() { const high = $('environment').value === 'Production' || $('access-level').value === 'Admin'; $('risk-preview').textContent = high ? 'High-risk · Manager → System Owner approval.' : 'Standard request · Manager approval.'; $('risk-preview').className = 'policy-box' + (high ? ' high' : ''); }
function openCreate() {
  $('create-form').reset(); inlineError('create-error', '');
  const pending = memoryPending || readPending();
  if (pending) { for (const key of ['application_id', 'environment', 'access_level', 'justification']) $('create-form').elements[key].value = pending[key]; inlineError('create-error', 'Submit sebelumnya belum terkonfirmasi. Kirim ulang isian ini menggunakan ClientRequestId yang sama.'); }
  $('requester-label').textContent = user().name; riskPreview(); $('create-dialog').showModal();
}
function empty(title, description, retry = false) {
  const state = $('list-state'); state.replaceChildren(el('strong', title), el('p', description)); state.hidden = false; $('table-wrap').hidden = true;
  if (retry) { const button = el('button', 'Coba lagi', 'button secondary'); button.onclick = loadList; state.append(button); }
}
async function loadList() {
  const generation = ++listGeneration, actor = currentUser, selectedScope = scope;
  $('list-state').textContent = 'Memuat permintaan…'; $('list-state').hidden = false; $('table-wrap').hidden = true; $('refresh').disabled = true;
  try {
    const {data: rows} = await api('/requests?scope=' + selectedScope, {}, actor);
    if (generation !== listGeneration) return;
    $('count-total').textContent = rows.length; $('count-pending').textContent = rows.filter(r => r.status.startsWith('Awaiting')).length; $('count-approved').textContent = rows.filter(r => r.status === 'Approved').length;
    if (!rows.length) { empty(selectedScope === 'inbox' ? 'Inbox sudah bersih.' : 'Belum ada permintaan.', selectedScope === 'mine' ? 'Buat request pertama Anda untuk memulai proses approval.' : 'Permintaan yang sesuai akan muncul di sini.'); return; }
    $('requests-body').replaceChildren();
    for (const r of rows) {
      const tr = el('tr'), application = el('td'); application.append(el('span', r.application_name, 'cell-main'), el('span', '#' + r.id.slice(0, 8).toUpperCase(), 'cell-sub')); tr.append(application);
      const requester = el('td'); requester.append(el('span', r.requester_name, 'cell-main'), el('span', r.requester_email, 'cell-sub')); tr.append(requester);
      const access = el('td'); access.append(el('span', r.access_level, 'access-chip'), el('span', r.environment, 'cell-sub')); if (r.high_risk) access.append(el('span', '◇ High-risk', 'risk-dot')); tr.append(access);
      const status = el('td'); status.append(badge(r.status)); if (r.assigned_approver) status.append(el('span', 'With ' + r.assigned_approver, 'risk-dot')); tr.append(status);
      tr.append(el('td', date(r.created_at), 'cell-sub')); const linkCell = el('td'), link = el('button', 'View →', 'row-link'); link.setAttribute('aria-label', 'Detail ' + r.application_name + ' ' + r.id.slice(0, 8)); link.onclick = () => openDetail(r.id); linkCell.append(link); tr.append(linkCell); $('requests-body').append(tr);
    }
    $('list-state').hidden = true; $('table-wrap').hidden = false;
  } catch (error) { if (generation === listGeneration) { empty('Daftar belum dapat dimuat.', describeError(error), true); for (const id of ['count-total','count-pending','count-approved']) $(id).textContent = '—'; } }
  finally { if (generation === listGeneration) $('refresh').disabled = false; }
}
function setScope(next) {
  scope = next; $('flash').hidden = true;
  const titles = {mine: ['My requests', 'Kelola permintaan akses dan ikuti setiap tahap approval.'], inbox: ['Approval inbox', 'Tinjau permintaan yang sedang menunggu keputusan Anda.'], all: ['Audit overview', 'Seluruh permintaan dan audit trail untuk pemeriksaan auditor.']};
  $('page-title').textContent = titles[scope][0]; $('page-subtitle').textContent = titles[scope][1]; $('list-title').textContent = scope === 'inbox' ? 'Needs your decision' : 'Request activity';
  document.querySelectorAll('[data-scope]').forEach(node => { node.classList.toggle('active', node.dataset.scope === scope); node.setAttribute('aria-current', node.dataset.scope === scope ? 'page' : 'false'); }); loadList();
}
function renderDetail(r) {
  activeRequest = r; $('detail-title').textContent = r.application_name + ' access'; $('detail-number').textContent = 'REQUEST #' + r.id.slice(0, 8).toUpperCase();
  $('detail-status').replaceChildren(badge(r.status), el('span', `Policy ${r.policy_version} · Version ${r.version}`), el('span', r.high_risk ? '◇ High-risk' : 'Standard risk'));
  $('detail-fields').replaceChildren();
  for (const [label, value] of [['Requester',r.requester_name + ' · ' + r.requester_email],['Application',r.application_name],['Environment',r.environment],['Access level',r.access_level],['Manager (snapshot)',r.manager_name],['System Owner (snapshot)',r.owner_name],['Current approver',r.assigned_approver || 'Completed'],['Created',date(r.created_at)],['ClientRequestId',r.client_request_id],['Request ID',r.id]]) { const pair = el('div'); pair.append(el('dt', label), el('dd', value)); $('detail-fields').append(pair); }
  $('detail-justification').textContent = r.justification; $('decision-panel').hidden = !r.can_act; $('reject-reason').value = ''; $('audit-timeline').replaceChildren();
  for (const event of r.audit) { const item = el('li'); item.append(el('strong', ({submitted:'Request submitted',approved:'Request approved',rejected:'Request rejected'})[event.action] + ' · ' + event.actor_name), el('time', date(event.occurred_at)), el('span', `${event.from_status ? labels[event.from_status] + ' → ' : ''}${labels[event.to_status]} · v${event.request_version}`, 'cell-sub')); if (event.reason) item.append(el('p', event.reason, 'reason')); $('audit-timeline').append(item); }
  $('detail-state').hidden = true; $('detail-content').hidden = false;
}
async function openDetail(id, preserveError = false) {
  const generation = ++detailGeneration, actor = currentUser; activeRequest = null; $('detail-state').textContent = 'Memuat detail…'; $('detail-state').hidden = false; $('detail-content').hidden = true; $('reload-detail').disabled = true;
  if (!preserveError) inlineError('detail-error', '');
  if (!$('detail-dialog').open) $('detail-dialog').showModal();
  try { const {data} = await api('/requests/' + id, {}, actor); if (generation === detailGeneration) renderDetail(data); }
  catch (error) { if (generation === detailGeneration) $('detail-state').textContent = describeError(error); }
  finally { if (generation === detailGeneration) { $('reload-detail').disabled = mutating; $('reload-detail').onclick = () => openDetail(id); } }
}
async function decide(action) {
  if (!activeRequest || mutating) return;
  const snapshot = activeRequest, actor = currentUser, reason = $('reject-reason').value.trim();
  if (action === 'reject' && !reason) { inlineError('detail-error', 'Alasan penolakan wajib diisi.'); $('reject-reason').focus(); return; }
  busy(true); inlineError('detail-error', '');
  try { const {data} = await api(`/requests/${snapshot.id}/${action}`, {method:'POST',body:JSON.stringify({version:snapshot.version,...(action === 'reject' ? {reason} : {})})}, actor); renderDetail(data); notify('Keputusan tersimpan. Status: ' + labels[data.status] + '.'); await loadList(); }
  catch (error) { inlineError('detail-error', describeError(error)); if (error.status === 409 || error.uncertain) { await openDetail(snapshot.id, true); await loadList(); } }
  finally { busy(false); }
}

$('create-form').addEventListener('submit', async event => {
  event.preventDefault(); if (mutating) return; inlineError('create-error', '');
  const form = new FormData(event.target), payload = Object.fromEntries(form); payload.justification = payload.justification.trim();
  if (!payload.justification) { inlineError('create-error', 'Justification wajib diisi.'); $('justification').focus(); return; }
  const pending = memoryPending || readPending();
  if (pending && Object.keys(payload).some(key => pending[key] !== payload[key])) { inlineError('create-error', 'Ada submit yang belum terkonfirmasi. Tutup dan buka form untuk mengirim ulang isian sebelumnya sebelum membuat request lain.'); return; }
  const body = pending || {...payload, client_request_id: crypto.randomUUID()}; memoryPending = body; setPending(body); busy(true); $('submit-request').textContent = 'Menyimpan…';
  try { const result = await api('/requests', {method:'POST',body:JSON.stringify(body)}); memoryPending = null; setPending(null); $('create-dialog').close(); notify(result.replayed ? 'Retry terkonfirmasi. Request yang sama ditampilkan tanpa membuat duplikat.' : 'Request terkirim ke Manager.'); await loadList(); await openDetail(result.data.id); }
  catch (error) { inlineError('create-error', describeError(error)); if (!error.uncertain) { memoryPending = null; setPending(null); } }
  finally { busy(false); $('submit-request').textContent = 'Submit request →'; }
});
document.querySelectorAll('.close-dialog').forEach(button => button.addEventListener('click', () => { if (!mutating) { button.closest('dialog').close(); ++detailGeneration; } }));
document.querySelectorAll('dialog').forEach(dialog => dialog.addEventListener('cancel', event => { if (mutating) event.preventDefault(); else ++detailGeneration; }));
document.querySelectorAll('[data-scope]').forEach(button => button.addEventListener('click', () => { if (!mutating) setScope(button.dataset.scope); }));
$('refresh').onclick = loadList; $('new-request').onclick = openCreate; $('environment').onchange = riskPreview; $('access-level').onchange = riskPreview; $('approve-button').onclick = () => decide('approve'); $('reject-button').onclick = () => decide('reject');
function applyUser() {
  $('user-select').value = currentUser;
  $('avatar').textContent = user().name[0];
  $('audit-nav').hidden = user().role !== 'Auditor';
  $('new-request').disabled = !user().manager_id;
  $('new-request').title = user().manager_id ? '' : 'Gunakan Alice: user lain belum memiliki manager.';
  setScope(user().role === 'Auditor' ? 'all' : user().role === 'Requester' ? 'mine' : 'inbox');
}
$('user-select').onchange = event => {
  if (mutating || !users.some(item => item.id === event.target.value)) return;
  ++detailGeneration; activeRequest = null; memoryPending = null; currentUser = event.target.value;
  // Per-tab persistence lets two tabs retain separate demo actors during testing.
  try { sessionStorage.setItem('accesshub-demo-user', currentUser); } catch (_) { /* Optional convenience. */ }
  document.querySelectorAll('dialog').forEach(dialog => dialog.close()); applyUser();
};
async function init() {
  try {
    const {data} = await api('/bootstrap', {}, 'alice'); users = data.users;
    try { const saved = sessionStorage.getItem('accesshub-demo-user'); if (users.some(item => item.id === saved)) currentUser = saved; } catch (_) { /* Default to Alice. */ }
    $('user-select').replaceChildren();
    for (const u of users) { const option = el('option', `${u.name} · ${u.role}`); option.value = u.id; $('user-select').append(option); }
    $('application').replaceChildren();
    for (const app of data.applications) { const option = el('option', app.name); option.value = app.id; $('application').append(option); }
    for (const [id, values] of [['environment', data.environments], ['access-level', data.access_levels]]) {
      $(id).replaceChildren();
      for (const value of values) { const option = el('option', value); option.value = value; $(id).append(option); }
    }
    $('user-select').disabled = false; applyUser();
  }
  catch (error) { empty('Aplikasi belum dapat dimuat.', describeError(error)); notify('Pastikan backend berjalan lalu refresh halaman.', true); }
}
init();
