// Optional frontend behavior checks. Requires Node.js 20+, no npm install.
// DOM and HTTP are simulated: these checks do not replace browser/.NET tests.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import vm from 'node:vm';
import { randomUUID } from 'node:crypto';

const root = new URL('../src/AccessRequestHub/wwwroot/', import.meta.url);
const source = await readFile(new URL('app.js', root), 'utf8');
const html = await readFile(new URL('index.html', root), 'utf8');
const bootstrap = {
  users: [
    {id: 'alice', name: 'Alice', role: 'Requester', manager_id: 'bob'},
    {id: 'bob', name: 'Bob', role: 'Manager'},
    {id: 'carol', name: 'Carol', role: 'SystemOwner'},
    {id: 'dana', name: 'Dana', role: 'SystemOwner'},
    {id: 'erin', name: 'Erin', role: 'Auditor'}
  ],
  applications: [{id: 'crm', name: 'CRM', owner_id: 'carol'}],
  environments: ['NonProduction', 'Production'], access_levels: ['Read', 'Admin']
};
const sample = (changes = {}) => ({
  id: 'request-0001', client_request_id: 'client-0001',
  application_name: 'CRM', requester_name: 'Alice', requester_email: 'alice@example.local',
  manager_name: 'Bob', owner_name: 'Carol', environment: 'NonProduction',
  access_level: 'Read', justification: 'Business reporting', status: 'AwaitingManager',
  policy_version: 'v1', version: 1, created_at: '2026-01-01T00:00:00Z',
  high_risk: false, can_act: false, assigned_approver: 'Bob', audit: [], ...changes
});
const response = (body, status = 200) => ({ok: status < 400, status, json: async () => body});
const flush = () => new Promise(resolve => setImmediate(resolve));
const deferred = () => { let resolve; const promise = new Promise(done => { resolve = done; }); return {promise, resolve}; };

class Element {
  constructor(tagName = 'div') {
    this.tagName = tagName; this.children = []; this.events = {}; this.dataset = {};
    this.value = ''; this.textContent = ''; this.hidden = false; this.disabled = false;
    this.open = false; this.classList = {toggle() {}};
  }
  append(...nodes) { this.children.push(...nodes); if (this.tagName === 'select' && !this.value && nodes[0]) this.value = nodes[0].value; }
  replaceChildren(...nodes) { this.children = []; this.textContent = ''; this.append(...nodes); }
  setAttribute(name, value) { this[name] = value; }
  addEventListener(name, callback) { this.events[name] = callback; }
  showModal() { this.open = true; }
  close() { this.open = false; }
  focus() { this.focused = true; }
}

async function harness(saved = {}) {
  const nodes = Object.fromEntries([...html.matchAll(/\bid="([^"]+)"/g)].map(match => [match[1], new Element()]));
  for (const id of ['user-select', 'application', 'environment', 'access-level']) nodes[id].tagName = 'select';
  const form = nodes['create-form'];
  form.elements = {application_id: nodes.application, environment: nodes.environment, access_level: nodes['access-level'], justification: nodes.justification};
  form.reset = () => { for (const [key, value] of Object.entries({application_id: 'crm', environment: 'NonProduction', access_level: 'Read', justification: ''})) form.elements[key].value = value; };
  form.reset();
  const nav = ['mine', 'inbox', 'all'].map(scope => {const node = new Element('button'); node.dataset.scope = scope; return node;});
  const storage = new Map(Object.entries(saved)), calls = [];
  const state = {
    nodes, storage, calls,
    handler: async url => url === '/api/bootstrap' ? response({data: bootstrap}) : response({data: []})
  };
  const context = vm.createContext({
    document: {
      getElementById(id) { assert.ok(nodes[id], 'Missing HTML id: ' + id); return nodes[id]; },
      createElement: tag => new Element(tag),
      querySelectorAll: selector => selector === '[data-scope]' ? nav : selector === 'dialog' ? [nodes['create-dialog'], nodes['detail-dialog']] : []
    },
    fetch: async (url, options) => {calls.push({url, options}); return state.handler(url, options);},
    sessionStorage: {getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value), removeItem: key => storage.delete(key)},
    FormData: class {constructor(target) {this.target = target;} *[Symbol.iterator]() {for (const [key, node] of Object.entries(this.target.elements)) yield [key, node.value];}},
    crypto: {randomUUID}, console
  });
  state.run = code => vm.runInContext(code, context);
  await state.run(source); await flush();
  state.submit = () => form.events.submit({preventDefault() {}, target: form});
  return state;
}

test('HTML identifiers are unique and assets comply with an external-script CSP', async () => {
  const ids = [...html.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]);
  assert.equal(new Set(ids).size, ids.length);
  assert.doesNotMatch(html, /\bon\w+\s*=|\bstyle\s*=|<style\b/i);
  assert.doesNotMatch(source, /\.innerHTML\s*=|insertAdjacentHTML\s*\(/);
  for (const file of ['app.js', 'styles.css', 'favicon.svg']) await readFile(new URL(file, root));
});

test('startup uses the API and demo identity, then renders the empty state', async () => {
  const h = await harness();
  assert.equal(h.calls[0].url, '/api/bootstrap');
  assert.equal(h.calls[0].options.headers['X-User-Id'], 'alice');
  assert.equal(h.calls[1].url, '/api/requests?scope=mine');
  assert.equal(h.nodes['count-total'].textContent, 0);
  assert.equal(h.nodes['list-state'].children[0].textContent, 'Belum ada permintaan.');
});

test('per-tab demo user restores auditor scope without enabling request creation', async () => {
  const h = await harness({'accesshub-demo-user': 'erin'});
  assert.equal(h.calls.at(-1).options.headers['X-User-Id'], 'erin');
  assert.equal(h.calls.at(-1).url, '/api/requests?scope=all');
  assert.equal(h.nodes['audit-nav'].hidden, false);
  assert.equal(h.nodes['new-request'].disabled, true);
});

test('a slow list from the previous actor cannot replace the current list', async () => {
  const h = await harness(), old = deferred();
  h.handler = () => old.promise;
  const pending = h.run('loadList()');
  h.handler = async () => response({data: []});
  h.nodes['user-select'].onchange({target: {value: 'bob'}}); await flush();
  old.resolve(response({data: [sample()]})); await pending;
  assert.equal(h.nodes['count-total'].textContent, 0);
  assert.equal(h.nodes['page-title'].textContent, 'Approval inbox');
  assert.equal(h.calls.at(-1).options.headers['X-User-Id'], 'bob');
});

test('a closed detail ignores a late response', async () => {
  const h = await harness(), delayed = deferred();
  h.handler = () => delayed.promise;
  const pending = h.run("openDetail('request-0001')");
  h.nodes['detail-dialog'].events.cancel({}); h.nodes['detail-dialog'].close();
  delayed.resolve(response({data: sample()})); await pending;
  assert.equal(h.run('activeRequest'), null);
  assert.equal(h.nodes['detail-content'].hidden, true);
});

test('uncertain submit and reload reuse the original ClientRequestId and payload', async () => {
  const h = await harness();
  h.nodes.justification.value = '  Monthly reporting  ';
  h.handler = async () => {throw new Error('connection lost');};
  await h.submit();
  const original = h.calls.at(-1).options.body;
  assert.equal(JSON.parse(original).justification, 'Monthly reporting');
  assert.ok(JSON.parse(original).client_request_id);
  assert.equal(h.storage.has('accesshub-pending-alice'), true);
  const restored = await harness(Object.fromEntries(h.storage));
  restored.run('openCreate()');
  restored.handler = async url => url === '/api/requests' ? response({data: sample(), replayed: true}) : response({data: url.includes('?') ? [] : sample()});
  await restored.submit();
  const replay = restored.calls.find(call => call.options.method === 'POST');
  assert.equal(replay.options.body, original);
  assert.equal(restored.storage.has('accesshub-pending-alice'), false);
  assert.equal(restored.nodes['create-dialog'].open, false);
});

test('double-submit is blocked while the first response is pending', async () => {
  const h = await harness(), delayed = deferred();
  h.nodes.justification.value = 'Business reporting';
  h.handler = (url, options) => options.method === 'POST' ? delayed.promise : Promise.resolve(response({data: url.includes('?') ? [] : sample()}));
  const first = h.submit(); await h.submit();
  assert.equal(h.calls.filter(call => call.options.method === 'POST').length, 1);
  assert.equal(h.nodes['user-select'].disabled, true);
  delayed.resolve(response({data: sample(), replayed: false}, 201)); await first;
  assert.equal(h.nodes['user-select'].disabled, false);
});

test('stale approval submits the displayed version and refreshes current truth', async () => {
  const h = await harness();
  h.handler = async (url, options) => options.method === 'POST'
    ? response({error: {code: 'stale_version', message: 'Data telah berubah.'}, request_id: 'trace-123456'}, 409)
    : response({data: url.includes('?') ? [] : sample({status: 'Approved', version: 2})});
  h.run("renderDetail({id:'request-0001',application_name:'CRM',requester_name:'Alice',requester_email:'alice@example.local',manager_name:'Bob',owner_name:'Carol',status:'AwaitingManager',version:1,policy_version:'v1',audit:[],created_at:'2026-01-01',can_act:true})");
  await h.run("decide('approve')");
  const posted = h.calls.find(call => call.options.method === 'POST');
  assert.equal(JSON.parse(posted.options.body).version, 1);
  assert.equal(h.run('activeRequest.version'), 2);
  assert.equal(h.nodes['decision-panel'].hidden, true);
  assert.match(h.nodes['detail-error'].textContent, /Data telah berubah/);
});

test('rejection requires a reason before an HTTP mutation', async () => {
  const h = await harness();
  h.run("activeRequest = {id:'request-0001',version:1}");
  h.nodes['reject-reason'].value = '  ';
  await h.run("decide('reject')");
  assert.equal(h.calls.some(call => call.options.method === 'POST'), false);
  assert.match(h.nodes['detail-error'].textContent, /wajib/);
});

test('justification is displayed as literal text', async () => {
  const h = await harness(), payload = '<img src=x onerror=alert(1)>';
  h.handler = async () => response({data: sample({justification: payload})});
  await h.run("openDetail('request-0001')");
  assert.equal(h.nodes['detail-justification'].textContent, payload);
  assert.equal(h.nodes['detail-justification'].children.length, 0);
});
