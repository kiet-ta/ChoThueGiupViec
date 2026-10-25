// Shared chrome of every prototype screen: status bar, bottom navbar, home indicator, chart tooltips.
// A screen declares <body data-role="customer|worker" data-tab="..."> and gets the role's navbar.
(function () {
  const NAV = {
    customer: [
      { tab: "home", icon: "home", label: "Trang chủ", href: "home.html" },
      { tab: "orders", icon: "receipt_long", label: "Đơn", href: "orders.html" },
      { tab: "book", icon: "add", label: "Đặt ca", href: "booking.html", fab: true },
      { tab: "favorites", icon: "favorite", label: "Yêu thích", href: "favorites.html" },
      { tab: "account", icon: "person", label: "Tài khoản", href: "account.html" },
    ],
    worker: [
      { tab: "today", icon: "today", label: "Hôm nay", href: "today.html" },
      { tab: "schedule", icon: "calendar_month", label: "Lịch", href: "schedule.html" },
      { tab: "job", icon: "work", label: "Việc", href: "job.html", fab: true, badge: true },
      { tab: "income", icon: "payments", label: "Thu nhập", href: "income.html" },
      { tab: "account", icon: "person", label: "Tài khoản", href: "account.html" },
    ],
  };

  function el(html) {
    const t = document.createElement("template");
    t.innerHTML = html.trim();
    return t.content.firstChild;
  }

  document.addEventListener("DOMContentLoaded", () => {
    if (window.self !== window.top) document.body.classList.add("embed");
    const phone = document.querySelector(".phone");
    if (!phone) return;

    if (!phone.querySelector(".statusbar")) {
      phone.prepend(el(
        '<div class="statusbar"><span class="tnum">9:41</span><span class="sys">' +
        '<span class="ms">signal_cellular_alt</span><span class="ms">wifi</span><span class="ms">battery_full</span></span></div>'));
    }

    const role = document.body.dataset.role;
    const tab = document.body.dataset.tab;
    if (role && tab && NAV[role]) {
      phone.classList.add("has-nav");
      const nav = el('<nav class="bnav"></nav>');
      NAV[role].forEach((it) => {
        if (it.fab) {
          nav.append(el(
            `<a class="fab-slot" href="${it.href}"><span class="fab"><span class="ms">${it.icon}</span>` +
            (it.badge ? '<span class="badge"></span>' : "") +
            `</span><span class="fab-label">${it.label}</span></a>`));
        } else {
          nav.append(el(
            `<a href="${it.href}" class="${it.tab === tab ? "on" : ""}"><span class="ms">${it.icon}</span>${it.label}</a>`));
        }
      });
      phone.append(nav);
    }
    phone.append(el('<div class="home-ind"></div>'));

    // Hover tooltip on chart marks: <rect data-tip="T6 · 4.160.000 đ">
    document.querySelectorAll(".chart").forEach((chart) => {
      const tip = el('<div class="tip"></div>');
      chart.append(tip);
      chart.querySelectorAll("[data-tip]").forEach((mark) => {
        mark.addEventListener("mouseenter", () => {
          const c = chart.getBoundingClientRect();
          const m = mark.getBoundingClientRect();
          tip.textContent = mark.dataset.tip;
          tip.style.left = m.left - c.left + m.width / 2 + "px";
          tip.style.top = m.top - c.top + "px";
          tip.style.display = "block";
        });
        mark.addEventListener("mouseleave", () => (tip.style.display = "none"));
      });
    });

    // Offer countdown: <span data-countdown="30">
    if (!location.search.includes("static")) document.querySelectorAll("[data-countdown]").forEach((node) => {
      let left = Number(node.dataset.countdown);
      const ring = document.querySelector("[data-ring]");
      const total = left;
      const tick = () => {
        node.textContent = left;
        if (ring) ring.style.strokeDashoffset = String(ring.dataset.len * (1 - left / total));
        if (left > 0) { left -= 1; setTimeout(tick, 1000); }
      };
      tick();
    });
  });
})();
