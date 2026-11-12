"use client";

import { use } from "react";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { Require } from "@/components/page-guard";
import { Pipeline } from "@/components/hr/recruitment";
import { t } from "@/i18n";

export default function PipelinePage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <Require perm="hrconfig.manage"><p style={{ marginBottom: 12 }}><Link href="/hr/recruitment" className="link-btn"><ArrowLeft size={14} /> {t("All job postings")}</Link></p><Pipeline id={id} /></Require>;
}
