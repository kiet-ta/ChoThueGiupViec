// Shared chrome of every prototype screen.
// A page declares <body data-role="customer|agency" data-tab="..." data-crumb="..."> and gets the role's navigation:
//   customer -> sticky top bar,  agency -> 240 px sidebar (decision D3) + slim top bar.
// Pages without data-role (auth screens, admin login) get no chrome.
(function () {
  const CUSTOMER = [
    { tab: "home", label: "Trang chủ", href: "home.html" },
    { tab: "orders", label: "Đơn của tôi", href: "orders.html" },
    { tab: "favorites", label: "Thợ yêu thích", href: "favorites.html" },
    { tab: "account", label: "Tài khoản", href: "account.html" },
  ];
  const AGENCY = [
    { tab: "overview", icon: "space_dashboard", label: "Tổng quan", href: "overview.html" },
    { section: "Tài khoản" },
    { tab: "subscription", icon: "workspace_premium", label: "Gói thuê bao", href: "subscription.html" },
    { tab: "escrow", icon: "account_balance", label: "Ký quỹ & SLA", href: "escrow.html" },
    { tab: "bank", icon: "credit_card", label: "Tài khoản nhận tiền", href: "bank.html" },
    { section: "Nhân sự" },
    { tab: "import", icon: "upload_file", label: "Thêm thợ", href: "import.html" },
    { tab: "workers", icon: "groups", label: "Danh sách thợ", href: "worker-list.html" },
    { tab: "roster", icon: "calendar_view_week", label: "Lịch ca tuần", href: "roster.html" },
    { section: "Vận hành" },
    { tab: "jobs", icon: "receipt_long", label: "Đơn & hiệu suất", href: "jobs.html" },
    { tab: "swap", icon: "swap_horiz", label: "Đổi thợ 30 phút", href: "swap.html" },
  ];

  function el(html) {
    const t = document.createElement("template");
    t.innerHTML = html.trim();
    return t.content.firstChild;
  }

  document.addEventListener("DOMContentLoaded", () => {
    if (window.self !== window.top) document.body.classList.add("embed");
    const role = document.body.dataset.role;
    const tab = document.body.dataset.tab;
    const main = document.querySelector("main");
    if (!role || !main) return;

    if (role === "customer") {
      const links = CUSTOMER.map(
        (i) => `<a href="${i.href}" class="${i.tab === tab ? "on" : ""}">${i.label}</a>`).join("");
      document.body.prepend(el(
        `<header class="topbar"><div class="in">
          <a class="brand" href="home.html"><span class="mark"><span class="ms">home</span></span>TỔ ẤM</a>
          <nav aria-label="Điều hướng chính">${links}</nav>
          <a class="btn sm" href="booking.html"><span class="ms">add</span>Đặt ca</a>
          <a class="icon-btn" href="disputes.html" aria-label="Thông báo"><span class="ms">notifications</span><span class="dot"></span></a>
          <a class="avatar" href="account.html" aria-label="Tài khoản">L</a>
        </div></header>`));
      return;
    }

    if (role === "agency") {
      const items = AGENCY.map((i) =>
        i.section
          ? `<div class="sec">${i.section}</div>`
          : `<a class="item ${i.tab === tab ? "on" : ""}" href="${i.href}"><span class="ms">${i.icon}</span>${i.label}</a>`).join("");
      const shell = el(
        `<div class="shell">
          <aside class="sidebar">
            <a class="brand" href="overview.html"><span class="mark"><span class="ms">home</span></span>TỔ ẤM<span class="pill" style="margin-left:auto;background:rgba(255,255,255,.14);color:#fff">Partner</span></a>
            ${items}
            <div class="foot"><b>Sạch Việt Co.</b>MST 0312345678<br><a href="login.html" style="color:#C5ECC9;font-weight:600">Đăng xuất</a></div>
          </aside>
          <div class="shell-main">
            <div class="shell-top"><div class="crumbs">Đối tác · <b>${document.body.dataset.crumb || ""}</b></div>
              <div class="row"><span class="pill ok"><span class="ms fill">verified</span>Verified Partner</span><span class="avatar sm">SV</span></div></div>
          </div></div>`);
      main.replaceWith(shell);
      shell.querySelector(".shell-main").append(main);
    }
  });
})();
