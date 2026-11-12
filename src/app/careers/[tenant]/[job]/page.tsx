"use client";

import { use } from "react";
import { CareerJobPage } from "@/components/careers";

export default function CareerJob({ params }: { params: Promise<{ tenant: string; job: string }> }) {
  const { tenant, job } = use(params);
  return <CareerJobPage tenant={tenant} job={job} />;
}
