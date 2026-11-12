"use client";

import { use } from "react";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { CoursePlayer } from "@/components/hr/learning";
import { t } from "@/i18n";

export default function CoursePage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <><p style={{ marginBottom: 12 }}><Link href="/hr/learning" className="link-btn"><ArrowLeft size={14} /> {t("My Learning")}</Link></p><CoursePlayer id={id} /></>;
}
