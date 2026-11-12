"use client";

import { use } from "react";
import { CareersList } from "@/components/careers";

export default function CareersPage({ params }: { params: Promise<{ tenant: string }> }) {
  const { tenant } = use(params);
  return <CareersList tenant={tenant} />;
}
