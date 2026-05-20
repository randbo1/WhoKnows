import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';

const parseCsv = (value) => value.split(',').map(x => x.trim()).filter(Boolean);

const parseActions = (value) => value
  .split('\n')
  .map(x => x.trim())
  .filter(Boolean)
  .map((line) => {
    const [type, name, numericValue] = line.split('|').map(x => (x ?? '').trim());
    return { type, name, numericValue: numericValue ? Number(numericValue) : null };
  });

function App() {
  const [questions, setQuestions] = useState([]);
  const [rules, setRules] = useState([]);
  const [clients, setClients] = useState([]);
  const [applications, setApplications] = useState([]);
  const [quote, setQuote] = useState(null);
  const [bordereau, setBordereau] = useState(null);

  const [newQuestion, setNewQuestion] = useState({ key: '', label: '', type: 'Number', options: '' });
  const [newRule, setNewRule] = useState({ id: '', name: '', questionKey: 'sum_insured', operator: 'GreaterThanOrEqual', value: '100000', actions: 'SetLimit|Property|100000' });
  const [newClientName, setNewClientName] = useState('');
  const [applicationForm, setApplicationForm] = useState({ clientId: '', productCode: 'CommercialProperty', answers: 'sum_insured=200000\nindustry=Technology\neffective_date=2026-07-01' });

  const load = async () => {
    const [q, r, c, a] = await Promise.all([
      fetch('/admin/questions').then(x => x.json()),
      fetch('/admin/rules').then(x => x.json()),
      fetch('/clients').then(x => x.json()),
      fetch('/applications').then(x => x.json()),
    ]);

    setQuestions(q.items ?? []);
    setRules(r.items ?? []);
    setClients(c.items ?? []);
    setApplications(a.items ?? []);
    if (!applicationForm.clientId && (c.items ?? []).length > 0) {
      setApplicationForm(x => ({ ...x, clientId: c.items[0].id }));
    }
  };

  useEffect(() => { load(); }, []);

  const addQuestion = async () => {
    await fetch('/admin/questions', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ question: { key: newQuestion.key, label: newQuestion.label, type: newQuestion.type, options: parseCsv(newQuestion.options) } }),
    });
    setNewQuestion({ key: '', label: '', type: 'Number', options: '' });
    await load();
  };

  const addRule = async () => {
    await fetch('/admin/rules', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({
        rule: {
          id: newRule.id,
          name: newRule.name,
          conditions: [{ questionKey: newRule.questionKey, operator: newRule.operator, value: newRule.value }],
          actions: parseActions(newRule.actions),
        },
      }),
    });
    setNewRule({ id: '', name: '', questionKey: 'sum_insured', operator: 'GreaterThanOrEqual', value: '100000', actions: 'SetLimit|Property|100000' });
    await load();
  };

  const addClient = async () => {
    const created = await fetch('/clients', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ name: newClientName }),
    }).then(x => x.json());

    setNewClientName('');
    await load();
    if (created.created?.id) {
      setApplicationForm(x => ({ ...x, clientId: created.created.id }));
    }
  };

  const createApplication = async () => {
    const answers = Object.fromEntries(
      applicationForm.answers
        .split('\n')
        .map(line => line.trim())
        .filter(Boolean)
        .map(line => {
          const idx = line.indexOf('=');
          if (idx <= 0) return ['', ''];
          return [line.slice(0, idx).trim(), line.slice(idx + 1).trim()];
        })
        .filter(([k]) => k)
    );

    const app = await fetch('/applications', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ clientId: applicationForm.clientId, productCode: applicationForm.productCode, answers }),
    }).then(x => x.json());

    if (app.created) {
      setApplications(x => [app.created, ...x]);
    }
  };

  const quoteApplication = async (id) => {
    const q = await fetch(`/applications/${id}/quote`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ applicationId: id }),
    }).then(x => x.json());
    setQuote(q);
  };

  const bindApplication = async (id) => {
    await fetch(`/applications/${id}/bind`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ applicationId: id }),
    });
    const b = await fetch('/reports/bordereau').then(x => x.json());
    setBordereau(b);
  };

  return (
    <main className="app">
      <h1>WhoKnows Dynamic Underwriting Platform</h1>
      <p className="note">Admin users can add questions/rules without coding. The same answers drive quotes, endorsements, limits, deductibles, and bordereau output.</p>

      <section className="grid">
        <article className="card">
          <h2>Admin: Questions</h2>
          <label>Key</label>
          <input value={newQuestion.key} onChange={e => setNewQuestion(x => ({ ...x, key: e.target.value }))} />
          <label>Label</label>
          <input value={newQuestion.label} onChange={e => setNewQuestion(x => ({ ...x, label: e.target.value }))} />
          <label>Type</label>
          <select value={newQuestion.type} onChange={e => setNewQuestion(x => ({ ...x, type: e.target.value }))}>
            <option>Number</option>
            <option>Date</option>
            <option>Selection</option>
          </select>
          <label>Options (comma separated)</label>
          <input value={newQuestion.options} onChange={e => setNewQuestion(x => ({ ...x, options: e.target.value }))} />
          <button onClick={addQuestion}>Save Question</button>
          <ul>{questions.map(q => <li key={q.key}><strong>{q.key}</strong> - {q.type}</li>)}</ul>
        </article>

        <article className="card">
          <h2>Admin: Quote Rules</h2>
          <label>Rule Id</label>
          <input value={newRule.id} onChange={e => setNewRule(x => ({ ...x, id: e.target.value }))} />
          <label>Name</label>
          <input value={newRule.name} onChange={e => setNewRule(x => ({ ...x, name: e.target.value }))} />
          <label>Question Key</label>
          <input value={newRule.questionKey} onChange={e => setNewRule(x => ({ ...x, questionKey: e.target.value }))} />
          <label>Operator</label>
          <select value={newRule.operator} onChange={e => setNewRule(x => ({ ...x, operator: e.target.value }))}>
            <option>Equals</option><option>NotEquals</option><option>GreaterThan</option><option>GreaterThanOrEqual</option>
            <option>LessThan</option><option>LessThanOrEqual</option><option>Before</option><option>After</option><option>In</option>
          </select>
          <label>Condition Value</label>
          <input value={newRule.value} onChange={e => setNewRule(x => ({ ...x, value: e.target.value }))} />
          <label>Actions (one per line: type|name|numericValue)</label>
          <textarea value={newRule.actions} onChange={e => setNewRule(x => ({ ...x, actions: e.target.value }))} />
          <button onClick={addRule}>Save Rule</button>
          <ul>{rules.map(r => <li key={r.id}><strong>{r.id}</strong> - {r.name}</li>)}</ul>
        </article>
      </section>

      <section className="grid" style={{ marginTop: '16px' }}>
        <article className="card">
          <h2>Underwriter Workflow</h2>
          <label>Client Name</label>
          <input value={newClientName} onChange={e => setNewClientName(e.target.value)} />
          <button onClick={addClient}>Create Client</button>
          <label>Client</label>
          <select value={applicationForm.clientId} onChange={e => setApplicationForm(x => ({ ...x, clientId: e.target.value }))}>
            {clients.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
          <label>Product Code</label>
          <input value={applicationForm.productCode} onChange={e => setApplicationForm(x => ({ ...x, productCode: e.target.value }))} />
          <label>Answers (question=value per line)</label>
          <textarea value={applicationForm.answers} onChange={e => setApplicationForm(x => ({ ...x, answers: e.target.value }))} />
          <button onClick={createApplication}>Create Application</button>
          <ul>
            {applications.map(a => (
              <li key={a.id}>
                <strong>{a.productCode}</strong> ({a.id.slice(0, 8)})
                <button onClick={() => quoteApplication(a.id)}>Quote</button>
                <button onClick={() => bindApplication(a.id)}>Bind</button>
              </li>
            ))}
          </ul>
        </article>

        <article className="card">
          <h2>Quote + Bordereau</h2>
          {quote ? <pre>{JSON.stringify(quote, null, 2)}</pre> : <p className="note">Run quote on an application to preview endorsements, limits and deductibles.</p>}
          {bordereau ? <><h3>Latest Bordereau</h3><pre>{bordereau.csv}</pre></> : null}
        </article>
      </section>
    </main>
  );
}

createRoot(document.getElementById('root')).render(<App />);
