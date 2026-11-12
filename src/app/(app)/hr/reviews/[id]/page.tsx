"use client";

import { use } from "react";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { ReviewFormView } from "@/components/hr/performance";
import { t } from "@/i18n";

export default function ReviewPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <><p style={{ marginBottom: 12 }}><Link href="/hr/reviews" className="link-btn"><ArrowLeft size={14} /> {t("All reviews")}</Link></p><ReviewFormView id={id} /></>;
}
