import type { MetadataRoute } from "next";

export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "Annaba HRIS",
    short_name: "Annaba HRIS",
    description: "Employees, attendance, leave, payroll and self-service in one place",
    start_url: "/dashboard",
    scope: "/",
    display: "standalone",
    orientation: "any",
    background_color: "#f5f8fe",
    theme_color: "#0b62e6",
    categories: ["business", "productivity"],
    icons: [
      { src: "/icons/192", sizes: "192x192", type: "image/png", purpose: "any" },
      { src: "/icons/512", sizes: "512x512", type: "image/png", purpose: "any" },
      { src: "/icons/maskable-512", sizes: "512x512", type: "image/png", purpose: "maskable" },
    ],
    shortcuts: [
      { name: "My Attendance", short_name: "Attendance", url: "/hr/my-attendance" },
      { name: "My Leave", short_name: "Leave", url: "/hr/my-leave" },
      { name: "My Payslips", short_name: "Payslips", url: "/hr/my-payslips" },
    ],
  };
}
