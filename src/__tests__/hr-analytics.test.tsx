import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { BarList, ColumnChart, Kpi, ShareBar } from "@/components/charts/analytics";
import { isGroup, leaves, nav, visibleNav, type NavGroup } from "@/config/nav";
import id from "@/i18n/id";

describe("the menu is organised around HR work", () => {
  const groups = nav.filter(isGroup) as NavGroup[];
  const titles = groups.map((g) => g.title);

  it("starts with home and the person's own HR, and keeps system administration last but one", () => {
    expect(nav[0]).toMatchObject({ label: "Home", href: "/dashboard" });
    expect(titles).toEqual(["My HR", "Approvals", "Workforce", "Attendance", "Leave", "Payroll", "Claims", "Recruitment", "Performance & Talent", "HR Setup", "Administration", "My Account"]);
  });

  it("every HR module opens with its analytics screen", () => {
    for (const [title, href] of [["Workforce", "/hr/analytics/people"], ["Attendance", "/hr/analytics/attendance"], ["Leave", "/hr/analytics/leave"], ["Payroll", "/hr/analytics/payroll"], ["Claims", "/hr/analytics/claims"]] as const) {
      const first = groups.find((g) => g.title === title)!.children[0];
      expect(first).toMatchObject({ href });
      expect("perm" in first && first.perm).toBeTruthy(); // organisation-wide figures are never open to everyone
    }
  });

  it("an employee without any permission sees home, their own HR and their account, and nothing else", () => {
    const mine = visibleNav(nav, () => false);
    expect(mine.map((n) => (isGroup(n) ? n.title : n.label))).toEqual(["Home", "My HR", "My Account"]);
    expect(leaves(mine).every((l) => !l.perm)).toBe(true);
  });

  it("no page is listed twice and every label has an Indonesian translation", () => {
    const all = leaves(nav); const hrefs = all.map((l) => l.href);
    expect(new Set(hrefs).size).toBe(hrefs.length);
    const dict = id as Record<string, string>;
    for (const text of [...all.map((l) => l.label), ...titles]) expect(dict[text], `no Indonesian text for "${text}"`).toBeTruthy();
  });
});

describe("analytics building blocks carry their numbers as text", () => {
  it("a KPI tile shows label, value and hint, and becomes a link when it leads somewhere", () => {
    render(<Kpi icon={<i />} label="Headcount" value="42" hint="active employees" href="/hr/employees" />);
    const link = screen.getByRole("link", { name: /Headcount/ });
    expect(link).toHaveAttribute("href", "/hr/employees"); expect(link).toHaveTextContent("42"); expect(link).toHaveTextContent("active employees");
  });

  it("ranked bars print every value and say so when there is nothing to rank", () => {
    const { rerender } = render(<BarList label="By department" rows={[{ label: "Engineering", value: 12, note: "3 requests" }, { label: "Finance", value: 4 }]} />);
    const list = screen.getByRole("list", { name: "By department" });
    expect(within(list).getAllByRole("listitem")).toHaveLength(2); expect(list).toHaveTextContent("Engineering"); expect(list).toHaveTextContent("12"); expect(list).toHaveTextContent("3 requests");
    rerender(<BarList label="By department" rows={[]} />);
    expect(screen.getByText("No data yet.")).toBeInTheDocument();
  });

  it("a column chart is an image with a name and a table of the same figures for screen readers", () => {
    render(<ColumnChart label="Attendance per day" valueName="Present" partName="Late" points={[{ label: "1 Nov", value: 20, part: 3 }, { label: "2 Nov", value: 18, part: 0 }]} />);
    expect(screen.getByRole("img", { name: "Attendance per day" })).toBeInTheDocument();
    const table = screen.getByRole("table", { name: "Attendance per day" });
    expect(within(table).getAllByRole("row")).toHaveLength(3); // header and two days
    expect(table).toHaveTextContent("1 Nov"); expect(table).toHaveTextContent("20"); expect(table).toHaveTextContent("3");
    expect(screen.getAllByText("Late").length).toBeGreaterThan(0); // the legend names the second colour
  });

  it("an all-zero series is reported as no data instead of an empty axis", () => {
    render(<ColumnChart label="Leave days" valueName="Days" points={[{ label: "Jan", value: 0 }, { label: "Feb", value: 0 }]} />);
    expect(screen.queryByRole("img")).not.toBeInTheDocument(); expect(screen.getByText("No data yet.")).toBeInTheDocument();
  });

  it("shares show value and percentage next to each name", () => {
    render(<ShareBar label="By place of work" parts={[{ label: "Office", value: 30, tone: "blue" }, { label: "Remote", value: 10, tone: "teal" }]} />);
    expect(screen.getByRole("img", { name: "By place of work" })).toBeInTheDocument();
    const items = screen.getAllByRole("listitem");
    expect(items[0]).toHaveTextContent("Office"); expect(items[0]).toHaveTextContent("75%"); expect(items[1]).toHaveTextContent("25%");
  });
});
