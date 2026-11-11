"use client";

import { useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { Overlay } from "@/components/ui/overlay";
import { del, get, post } from "@/lib/api/client";
import type { TaskItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

/** ONB-002 — onboarding and offboarding tasks of one employee. Ticking is for people who may edit employees; everyone with access to the page can read. */
export function ChecklistTab({ employeeId, canEdit }: { employeeId: string; canEdit: boolean }) {
  const run = useRun(); const q = useQuery(() => get<TaskItem[]>(`/api/hr/employees/${employeeId}/checklist`), [employeeId]);
  const [open, setOpen] = useState(false); const [f, setF] = useState({ kind: "onboarding", title: "", dueDate: "" }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const tasks = q.data ?? [];
  const toggle = async (task: TaskItem) => { if ((await run(() => post(`/api/hr/employees/checklist/${task.id}/${task.done ? "reopen" : "done"}`))).ok) q.reload(); };
  const remove = async (task: TaskItem) => { if ((await run(() => del(`/api/hr/employees/checklist/${task.id}`), t("Task removed."))).ok) q.reload(); };
  async function add(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post(`/api/hr/employees/${employeeId}/checklist`, { kind: f.kind, title: f.title, dueDate: f.dueDate || null }), t("Task added."), { silent: true });
    if (r.ok) { setOpen(false); setF({ ...f, title: "", dueDate: "" }); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const groups = (["onboarding", "offboarding"] as const).map((k) => ({ k, items: tasks.filter((x) => x.kind === k) })).filter((g) => g.items.length > 0);
  return (
    <div className="stack">
      {q.error && <div className="form-alert err" role="alert">{q.error.message}</div>}
      {groups.length === 0 && !q.loading && <p className="tab-placeholder">{t("No tasks.")}</p>}
      {groups.map((g) => (
        <div key={g.k}>
          <h3>{g.k === "onboarding" ? t("Onboarding") : t("Offboarding")} <small className="sub">{g.items.filter((x) => x.done).length}/{g.items.length}</small></h3>
          <ul className="mini-list">
            {g.items.map((x) => (
              <li key={x.id} style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 12 }}>
                <label className="row" style={{ gap: 10, alignItems: "center", flex: 1, minWidth: 0 }}>
                  <Checkbox checked={x.done} disabled={!canEdit} onChange={() => toggle(x)} aria-label={x.title} />
                  <span style={{ textDecoration: x.done ? "line-through" : undefined }}>{x.title}</span>
                </label>
                <span className="row" style={{ gap: 8, flexShrink: 0, alignItems: "center" }}>
                  {x.overdue && <Badge tone="red">{t("Overdue")}</Badge>}{x.dueDate && <small className="sub">{t("Due")} {fmtDate(x.dueDate)}</small>}
                  {canEdit && <Button size="sm" variant="danger-outline" aria-label={t("Remove {name}", { name: x.title })} onClick={() => remove(x)}><Trash2 size={13} /></Button>}
                </span>
              </li>))}
          </ul>
        </div>))}
      {canEdit && <div><Button onClick={() => { setErr({}); setForm(""); setOpen(true); }}><Plus size={14} /> {t("Add task")}</Button></div>}
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("Add task")} footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="task-form">{t("Add")}</Button></>}>
        <form id="task-form" className="stack" noValidate onSubmit={add}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("List")} htmlFor="tk-kind" error={err.kind}><SelectInput id="tk-kind" value={f.kind} onChange={(e) => setF({ ...f, kind: e.target.value })} options={[{ value: "onboarding", label: t("Onboarding") }, { value: "offboarding", label: t("Offboarding") }]} /></Field>
          <Field label={t("Task")} htmlFor="tk-title" required error={err.title}><TextInput id="tk-title" data-autofocus value={f.title} invalid={!!err.title} onChange={(e) => setF({ ...f, title: e.target.value })} /></Field>
          <Field label={t("Due date")} htmlFor="tk-due"><TextInput id="tk-due" type="date" value={f.dueDate} onChange={(e) => setF({ ...f, dueDate: e.target.value })} /></Field>
        </form>
      </Overlay>
    </div>
  );
}
