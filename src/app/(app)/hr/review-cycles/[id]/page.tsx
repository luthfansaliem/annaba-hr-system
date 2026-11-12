"use client";

import { use } from "react";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { Require } from "@/components/page-guard";
import { CycleResults } from "@/components/hr/performance";
import { t } from "@/i18n";

export default function CycleResultsPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <Require perm="hrconfig.manage"><p style={{ marginBottom: 12 }}><Link href="/hr/review-cycles" className="link-btn"><ArrowLeft size={14} /> {t("All review cycles")}</Link></p><CycleResults id={id} /></Require>;
}
