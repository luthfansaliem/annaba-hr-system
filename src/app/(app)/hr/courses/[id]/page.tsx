"use client";

import { use } from "react";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { Require } from "@/components/page-guard";
import { CourseManager } from "@/components/hr/learning";
import { t } from "@/i18n";

export default function CourseManagePage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <Require perm="hrconfig.manage"><p style={{ marginBottom: 12 }}><Link href="/hr/courses" className="link-btn"><ArrowLeft size={14} /> {t("All courses")}</Link></p><CourseManager id={id} /></Require>;
}
