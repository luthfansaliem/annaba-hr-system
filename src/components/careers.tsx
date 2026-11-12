"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { ArrowLeft, Briefcase, CheckCircle2, FileText, MapPin, Send, Timer } from "lucide-react";
import { LogoMark } from "@/components/logo";
import type { CareerJob, CareerPage, PublicOffer, PublicTest } from "@/lib/api/types";
import { typeLabel } from "@/components/hr/employee-status";
import { toBase64 } from "@/lib/file";
import { fmtDate, idr } from "@/lib/format";
import { t } from "@/i18n";

/** The public pages talk to /api/careers without a session; a candidate's private link carries its token after "#" (never sent in the address) and the page sends it in a header. */
async function call<T>(path: string, init: RequestInit = {}, token?: string): Promise<T> {
  const headers: Record<string, string> = { ...(init.body ? { "Content-Type": "application/json" } : {}), ...(token ? { "X-Candidate-Token": token } : {}) };
  const r = await fetch(path, { ...init, headers, cache: "no-store", credentials: "omit" });
  if (r.status === 204) return undefined as T;
  const body = await r.json().catch(() => null);
  if (!r.ok) { const e = new Error(r.status === 429 ? t("Too many tries. Wait a few minutes and try again.") : body?.error?.message ?? t("Something went wrong.")) as Error & { fields?: Record<string, string>; status?: number }; e.fields = body?.error?.fields ?? undefined; e.status = r.status; throw e; }
  return body as T;
}
const salaryText = (j: CareerJob) => (j.salaryMin != null || j.salaryMax != null ? (j.salaryMin != null && j.salaryMax != null ? `${idr(j.salaryMin)} – ${idr(j.salaryMax)}` : idr(j.salaryMin ?? j.salaryMax)) : null);
const Head = ({ company, sub }: { company: string; sub?: string }) => <div className="careers-head"><LogoMark size={38} /><div><h1>{company}</h1><small className="sub">{sub ?? t("Careers")}</small></div></div>;

export function CareersList({ tenant }: { tenant: string }) {
  const [d, setD] = useState<CareerPage | null>(null); const [error, setError] = useState("");
  useEffect(() => { call<CareerPage>(`/api/careers/${encodeURIComponent(tenant)}`).then(setD, (e) => setError(e.message)); }, [tenant]);
  if (error) return <main className="careers"><div className="form-alert err" role="alert">{error}</div></main>;
  if (!d) return <main className="careers"><p className="sub">{t("Loading…")}</p></main>;
  return (
    <main className="careers">
      <Head company={d.company} sub={t("Careers at {company}", { company: d.company })} />
      <h2 style={{ marginBottom: 4 }}>{t("Open positions")}</h2>
      <p className="sub" style={{ marginBottom: 14 }}>{d.jobs.length === 1 ? t("1 open position. Choose it to read more and apply.") : t("{n} open positions. Choose one to read more and apply.", { n: d.jobs.length })}</p>
      {d.jobs.length === 0 ? <p className="sub">{t("There are no open positions right now. Please check again later.")}</p>
        : d.jobs.map((j) => (
          <Link key={j.id} href={`/careers/${tenant}/${j.id}`} className="job-card">
            <h3>{j.title}</h3>
            <small className="sub">{[j.department, j.location, typeLabel(j.type), salaryText(j), j.closesOn ? t("apply by {date}", { date: fmtDate(j.closesOn) }) : null].filter(Boolean).join(" · ")}</small>
          </Link>))}
    </main>
  );
}

export function CareerJobPage({ tenant, job }: { tenant: string; job: string }) {
  const [j, setJ] = useState<CareerJob | null>(null); const [company, setCompany] = useState(""); const [error, setError] = useState("");
  const [f, setF] = useState({ fullName: "", email: "", phone: "", coverLetter: "", cvName: "", cvBase64: "", consent: false }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState(""); const [busy, setBusy] = useState(false); const [sent, setSent] = useState(false);
  useEffect(() => {
    call<CareerJob>(`/api/careers/${encodeURIComponent(tenant)}/${encodeURIComponent(job)}`).then(setJ, (e) => setError(e.message));
    call<CareerPage>(`/api/careers/${encodeURIComponent(tenant)}`).then((p) => setCompany(p.company), () => undefined);
  }, [tenant, job]);
  async function apply(e: React.FormEvent) {
    e.preventDefault(); setBusy(true); setForm(""); setErr({});
    try { await call(`/api/careers/${encodeURIComponent(tenant)}/${encodeURIComponent(job)}/apply`, { method: "POST", body: JSON.stringify(f) }); setSent(true); }
    catch (x) { const ex = x as Error & { fields?: Record<string, string> }; setErr(ex.fields ?? {}); setForm(ex.message); } finally { setBusy(false); }
  }
  if (error) return <main className="careers"><div className="form-alert err" role="alert">{error}</div><Link href={`/careers/${tenant}`} className="link-btn"><ArrowLeft size={14} /> {t("All positions")}</Link></main>;
  if (!j) return <main className="careers"><p className="sub">{t("Loading…")}</p></main>;
  return (
    <main className="careers">
      <Head company={company} />
      <p style={{ marginBottom: 12 }}><Link href={`/careers/${tenant}`} className="link-btn"><ArrowLeft size={14} /> {t("All positions")}</Link></p>
      <h2>{j.title}</h2>
      <p className="sub" style={{ margin: "6px 0 18px" }}><Briefcase size={14} /> {[j.department, typeLabel(j.type)].filter(Boolean).join(" · ")}{j.location && <> · <MapPin size={14} /> {j.location}</>}{salaryText(j) && <> · {salaryText(j)}</>}</p>
      <div className="card" style={{ padding: 18, marginBottom: 16 }}><div className="job-body">{j.description}</div>
        {j.requirements && <><h3 style={{ margin: "16px 0 6px" }}>{t("Requirements")}</h3><div className="job-body">{j.requirements}</div></>}</div>
      <div className="card" style={{ padding: 18 }}>
        {sent ? <div className="stack" role="status"><h3><CheckCircle2 size={20} /> {t("Thank you, your application was sent.")}</h3><p>{t("We emailed you a confirmation and will contact you about the next steps.")}</p></div> : (
          <form className="stack" noValidate onSubmit={apply}>
            <h3>{t("Apply for this job")}</h3>
            {form && <div className="form-alert err" role="alert">{form}</div>}
            <label className="auth-label" htmlFor="ap-name">{t("Full name")}</label><input id="ap-name" className="input" autoComplete="name" value={f.fullName} onChange={(e) => setF({ ...f, fullName: e.target.value })} />{err.fullName && <small className="err">{err.fullName}</small>}
            <label className="auth-label" htmlFor="ap-mail">{t("Email")}</label><input id="ap-mail" className="input" type="email" autoComplete="email" value={f.email} onChange={(e) => setF({ ...f, email: e.target.value })} />{err.email && <small className="err">{err.email}</small>}
            <label className="auth-label" htmlFor="ap-phone">{t("Phone")}</label><input id="ap-phone" className="input" type="tel" autoComplete="tel" value={f.phone} onChange={(e) => setF({ ...f, phone: e.target.value })} />
            <label className="auth-label" htmlFor="ap-cv">{t("CV (PDF, up to 4 MB)")}</label><input id="ap-cv" type="file" accept="application/pdf" onChange={async (e) => { const file = e.target.files?.[0]; if (file) setF({ ...f, cvName: file.name, cvBase64: await toBase64(file) }); }} />{err.cv && <small className="err">{err.cv}</small>}
            <label className="auth-label" htmlFor="ap-cl">{t("Why are you interested? (optional)")}</label><textarea id="ap-cl" className="textarea" rows={5} maxLength={4000} value={f.coverLetter} onChange={(e) => setF({ ...f, coverLetter: e.target.value })} />
            <label className="check"><input type="checkbox" checked={f.consent} onChange={(e) => setF({ ...f, consent: e.target.checked })} /> {t("I agree that {company} keeps my data and CV to process this and similar applications. I can ask for it to be deleted.", { company: company || t("the company") })}</label>{err.consent && <small className="err">{err.consent}</small>}
            <button className="signin" type="submit" disabled={busy}><Send size={16} /> {t("Send application")}</button>
          </form>)}
      </div>
    </main>
  );
}

function useHashToken() {
  const [token, setToken] = useState<string | null>(null);
  useEffect(() => { const h = window.location.hash.slice(1); if (h) { sessionStorage.setItem(`cand:${window.location.pathname}`, h); history.replaceState(null, "", window.location.pathname); } setToken(h || sessionStorage.getItem(`cand:${window.location.pathname}`) || ""); }, []);
  return token;
}

export function CandidateTest() {
  const token = useHashToken(); const [d, setD] = useState<PublicTest | null>(null); const [error, setError] = useState(""); const [answers, setAnswers] = useState<Record<string, number>>({}); const [busy, setBusy] = useState(false); const [done, setDone] = useState(false); const [now, setNow] = useState(Date.now());
  useEffect(() => { if (token === null) return; if (!token) { setError(t("This link is not complete. Open it again from the email.")); return; } call<PublicTest>("/api/careers/test", {}, token).then(setD, (e) => setError(e.message)); }, [token]);
  useEffect(() => { const i = setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(i); }, []);
  const start = async () => { setBusy(true); try { setD(await call<PublicTest>("/api/careers/test/start", { method: "POST" }, token!)); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } };
  const submit = async () => { setBusy(true); setError(""); try { await call("/api/careers/test/submit", { method: "POST", body: JSON.stringify({ answers }) }, token!); setDone(true); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } };
  const left = d?.startedAt && d.timeLimitMinutes ? Math.max(0, new Date(d.startedAt).getTime() + d.timeLimitMinutes * 60000 - now) : null;
  return (
    <main className="careers">
      {d && <Head company={d.company} />}
      {error && <div className="form-alert err" role="alert">{error}</div>}
      {d && (done || d.done ? <div className="card" style={{ padding: 18 }} role="status"><h3><CheckCircle2 size={20} /> {t("Thank you, {name}. Your answers were sent.", { name: d.candidate })}</h3><p>{t("The recruiter will contact you about the next steps.")}</p></div>
        : !d.startedAt ? <div className="card stack" style={{ padding: 18 }}>
            <h2>{d.name}</h2><p>{t("Hello {name}.", { name: d.candidate })} {d.kind === "test" ? t("This is a short test for your application.") : t("This questionnaire is about how you like to work. There are no right or wrong answers.")}</p>
            {d.timeLimitMinutes > 0 && <p><Timer size={16} /> {t("You have {n} minutes once you start, and can send your answers only once.", { n: d.timeLimitMinutes })}</p>}
            <button className="signin" type="button" disabled={busy} onClick={start}>{t("Start")}</button></div>
        : <div className="stack">
            <h2>{d.name}</h2>
            {left !== null && <p className={left < 60000 ? "neg" : "sub"}><Timer size={16} /> {t("Time left: {m} min {s} s", { m: Math.floor(left / 60000), s: Math.floor((left % 60000) / 1000) })}</p>}
            {d.questions.map((q, i) => (
              <fieldset key={q.id} className="test-q"><legend><b>{i + 1}. {q.text}</b></legend>
                {q.options.map((o, k) => <label key={k}><input type="radio" name={q.id} checked={answers[q.id] === k} onChange={() => setAnswers({ ...answers, [q.id]: k })} /> <span>{o}</span></label>)}
              </fieldset>))}
            <p className="sub">{t("{n} of {total} answered", { n: Object.keys(answers).length, total: d.questions.length })}</p>
            <button className="signin" type="button" disabled={busy || Object.keys(answers).length < d.questions.length} onClick={submit}><Send size={16} /> {t("Send answers")}</button>
          </div>)}
    </main>
  );
}

export function CandidateOffer() {
  const token = useHashToken(); const [d, setD] = useState<PublicOffer | null>(null); const [error, setError] = useState(""); const [note, setNote] = useState(""); const [busy, setBusy] = useState(false);
  useEffect(() => { if (token === null) return; if (!token) { setError(t("This link is not complete. Open it again from the email.")); return; } call<PublicOffer>("/api/careers/offer", {}, token).then(setD, (e) => setError(e.message)); }, [token]);
  const respond = async (accept: boolean) => { setBusy(true); setError(""); try { setD(await call<PublicOffer>("/api/careers/offer/respond", { method: "POST", body: JSON.stringify({ accept, note }) }, token!)); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } };
  return (
    <main className="careers">
      {d && <Head company={d.company} />}
      {error && <div className="form-alert err" role="alert">{error}</div>}
      {d && <div className="card stack" style={{ padding: 18 }}>
        <h2><FileText size={20} /> {t("Job offer")}</h2>
        <p>{t("Dear {name},", { name: d.candidate })}<br />{t("We are pleased to offer you the position of {job}.", { job: d.jobTitle })}</p>
        <div className="kv"><span>{t("Monthly salary (gross)")}</span><b>{idr(d.monthlySalary)}</b><span>{t("Employment type")}</span><b>{typeLabel(d.type)}{d.contractEnd ? ` · ${t("until {date}", { date: fmtDate(d.contractEnd) })}` : ""}</b><span>{t("Start date")}</span><b>{fmtDate(d.startDate)}</b><span>{t("Answer by")}</span><b>{fmtDate(d.expiresOn)}</b></div>
        {d.terms && <div className="job-body">{d.terms}</div>}
        {d.status === "sent" ? <>
          <label className="auth-label" htmlFor="of-note">{t("Message to the recruiter (optional)")}</label><textarea id="of-note" className="textarea" rows={3} maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} />
          <div className="row" style={{ gap: 10 }}><button className="signin" type="button" disabled={busy} onClick={() => respond(true)}>{t("Accept offer")}</button><button className="signin" type="button" style={{ background: "var(--navy)" }} disabled={busy} onClick={() => respond(false)}>{t("Decline")}</button></div></>
          : <div className="form-alert" role="status">{d.status === "accepted" ? t("You accepted this offer. Welcome aboard! HR will contact you about your first day.") : d.status === "declined" ? t("You declined this offer. Thank you for letting us know.") : t("This offer is no longer open.")}</div>}
      </div>}
    </main>
  );
}
