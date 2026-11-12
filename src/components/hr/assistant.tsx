"use client";

import { useEffect, useRef, useState } from "react";
import { Bot, Send, ShieldCheck, Sparkles, User } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Checkbox, Field } from "@/components/ui/field";
import { EmptyState } from "@/components/data/empty-state";
import { QueryBoundary } from "@/components/query-boundary";
import { get, post, put } from "@/lib/api/client";
import type { AssistantSettings, AssistantStatus, AssistantTurn } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDateTime } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const SUGGESTIONS = ["How many days of annual leave do I have left?", "Was I late this month?", "What does the policy say about sick leave?", "When is the next public holiday?"];

/** The chat. The conversation lives only in this page; the server keeps no copy of questions or answers. */
export function AssistantChat() {
  const status = useQuery(() => get<AssistantStatus>("/api/hr/assistant/status"), []);
  const [turns, setTurns] = useState<AssistantTurn[]>([]); const [q, setQ] = useState(""); const [busy, setBusy] = useState(false); const [error, setError] = useState("");
  const end = useRef<HTMLDivElement>(null);
  useEffect(() => { end.current?.scrollIntoView({ behavior: "smooth", block: "end" }); }, [turns, busy]);
  async function ask(question: string) {
    const text = question.trim(); if (!text || busy) return;
    setBusy(true); setError(""); setQ("");
    const history = turns.slice(-6); setTurns([...turns, { role: "user", text }]);
    try { const r = await post<{ answer: string }>("/api/hr/assistant/ask", { question: text, history }); setTurns((x) => [...x, { role: "assistant", text: r.answer }]); }
    catch (e) { setError((e as Error).message); setTurns((x) => x.slice(0, -1)); setQ(text); }
    finally { setBusy(false); }
  }
  return (
    <QueryBoundary q={status}>{(s) => !s.available ? (
      <Panel><EmptyState title={t("The HR assistant is not turned on")} description={s.configured ? t("Your administrator can turn it on under Administration → HR Assistant.") : t("The server has no language-model key yet; the assistant stays off until the system administrator adds one.")} /></Panel>
    ) : (
      <Panel>
        <div className="chat" aria-live="polite">
          {turns.length === 0 && <div className="chat-hello"><Sparkles size={22} /><p>{t("Ask about the company's HR policy, or about your own leave, attendance and claim limits{pay}.", { pay: s.payData ? t(" and payslips") : "" })}</p>
            <div className="chips">{SUGGESTIONS.map((x) => <button key={x} type="button" className="chip-btn" onClick={() => ask(t(x))}>{t(x)}</button>)}</div></div>}
          {turns.map((m, i) => (
            <div key={i} className={`chat-msg ${m.role}`}><span className="chat-ic" aria-hidden>{m.role === "user" ? <User size={16} /> : <Bot size={16} />}</span><div className="chat-bubble">{m.text}</div></div>))}
          {busy && <div className="chat-msg assistant"><span className="chat-ic" aria-hidden><Bot size={16} /></span><div className="chat-bubble sub">{t("Thinking…")}</div></div>}
          <div ref={end} />
        </div>
        {error && <div className="form-alert err" role="alert">{error}</div>}
        <form className="chat-input" onSubmit={(e) => { e.preventDefault(); ask(q); }}>
          <textarea aria-label={t("Your question")} className="textarea" rows={2} maxLength={2000} value={q} onChange={(e) => setQ(e.target.value)} onKeyDown={(e) => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); ask(q); } }} placeholder={t("Type your question…")} />
          <Button variant="primary" type="submit" disabled={busy || !q.trim()}><Send size={16} /> {t("Ask")}</Button>
        </form>
        <small className="sub"><ShieldCheck size={13} /> {t("Answers come from an AI model ({model}) and can be wrong; check important things with HR. Your question and the data the assistant reads for you are sent to the model provider; nothing is stored here.", { model: s.model ?? "" })}</small>
      </Panel>)}</QueryBoundary>
  );
}

export function AssistantAdmin() {
  const run = useRun(); const q = useQuery(() => get<AssistantSettings>("/api/hr/assistant/settings"), []);
  const [f, setF] = useState<{ enabled: boolean; policyText: string; allowPayData: boolean } | null>(null); const [form, setForm] = useState("");
  return <QueryBoundary q={q}>{(d) => { const s = f ?? { enabled: d.enabled, policyText: d.policyText, allowPayData: d.allowPayData }; return (
    <div className="stack">
      <Panel title={t("Status")} actions={d.configured ? <Badge tone="green">{t("Model key present")}</Badge> : <Badge tone="gray">{t("No model key")}</Badge>}>
        <p>{d.configured ? t("The server can reach the language model ({model}). Turn the assistant on for your organisation below.", { model: d.model ?? "" }) : t("The server has no language-model key. The system administrator sets ANTHROPIC_API_KEY (or Ai__AnthropicApiKey) in the server's environment; until then the assistant stays off.")}</p>
        {d.updatedAt && <small className="sub">{t("Last changed {when}", { when: fmtDateTime(d.updatedAt) })}</small>}
      </Panel>
      <Panel title={t("Settings")}>
        <form className="stack" onSubmit={async (e) => { e.preventDefault(); setForm(""); const r = await run(() => put<AssistantSettings>("/api/hr/assistant/settings", s), t("Assistant settings saved."), { silent: true }); if (r.ok) { q.setData(r.data); setF(null); } else setForm(r.error); }}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <label className="check"><Checkbox checked={s.enabled} disabled={!d.configured && !s.enabled} onChange={(e) => setF({ ...s, enabled: e.target.checked })} /> {t("Turn the HR assistant on for everyone in the organisation")}</label>
          <label className="check"><Checkbox checked={s.allowPayData} onChange={(e) => setF({ ...s, allowPayData: e.target.checked })} /> {t("Let it read a person's own payslip totals (gross and net) when they ask")}</label>
          <Field label={t("HR policy the assistant answers from")} htmlFor="as-policy" hint={t("Paste the company regulations, leave and claim rules, and frequent questions. Up to 400,000 characters. Do not paste personal data.")}>
            <textarea id="as-policy" className="textarea" rows={16} maxLength={400000} value={s.policyText} onChange={(e) => setF({ ...s, policyText: e.target.value })} /></Field>
          <small className="sub">{t("{n} characters", { n: s.policyText.length.toLocaleString() })}</small>
          <div><Button variant="primary" type="submit" disabled={!f}>{t("Save")}</Button></div>
        </form>
      </Panel>
      <Panel title={t("What it can and cannot do")}>
        <ul className="mini-list">
          <li><div><strong>{t("Reads only the asker's own data")}</strong><small>{t("Leave balances, attendance, claim limits and (if allowed) payslip totals, through the same checks as their own pages. Never other people's data, salaries or reviews.")}</small></div></li>
          <li><div><strong>{t("Changes nothing")}</strong><small>{t("It points to the right page to apply for leave, claim or correct attendance.")}</small></div></li>
          <li><div><strong>{t("Keeps nothing")}</strong><small>{t("Questions and answers are not stored. Each question is recorded in the audit log with its length, the data it read and the tokens used.")}</small></div></li>
          <li><div><strong>{t("Limited")}</strong><small>{t("30 questions per person per hour, 1,000 per organisation per day.")}</small></div></li>
        </ul>
      </Panel>
    </div>); }}</QueryBoundary>;
}
