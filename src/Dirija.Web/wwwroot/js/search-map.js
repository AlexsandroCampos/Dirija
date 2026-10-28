// Dirija! — tela de busca: mapa Leaflet + cards sincronizados.
(function () {
  "use strict";

  const PAULISTA = [-23.5614, -46.6559];
  const form = document.getElementById("search-form");
  const cardsEl = document.getElementById("cards");
  const emptyEl = document.getElementById("empty");
  const errorEl = document.getElementById("load-error");
  const titleEl = document.getElementById("results-title");

  const map = L.map("map", { zoomControl: true }).setView(PAULISTA, 13);
  L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
  }).addTo(map);

  const markersLayer = L.layerGroup().addTo(map);
  let originMarker = null;
  let radiusCircle = null;
  let geo = null; // {lat, lng} when the user shares location
  const markers = new Map(); // instructorId -> marker
  const cards = new Map();   // instructorId -> card element

  function priceIcon(label, active) {
    const span = document.createElement("span");
    span.textContent = label;
    return L.divIcon({ className: "price-pin" + (active ? " active" : ""), html: span.outerHTML, iconSize: [0, 0] });
  }

  function setActive(id, opts) {
    markers.forEach((m, key) => {
      const on = key === id;
      m.setIcon(priceIcon(m.options.priceLabel, on));
      m.setZIndexOffset(on ? 1000 : 0);
    });
    cards.forEach((c, key) => c.classList.toggle("active", key === id));
    if (opts && opts.scrollCard && cards.has(id)) {
      cards.get(id).scrollIntoView({ behavior: "smooth", block: "nearest", inline: "start" });
    }
    if (opts && opts.panMap && markers.has(id)) {
      map.panTo(markers.get(id).getLatLng());
    }
  }

  function stars(rating) {
    const full = Math.round(rating);
    return "★★★★★".slice(0, full) + "☆☆☆☆☆".slice(0, 5 - full);
  }

  function el(tag, cls, text) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text !== undefined) e.textContent = text;
    return e;
  }

  function buildCard(item) {
    const card = el("article", "card-dj instructor-card");
    card.tabIndex = 0;
    card.setAttribute("aria-label", `${item.name}, ${item.priceLabel} por aula, nota ${item.ratingLabel}, a ${item.distanceLabel}`);

    const top = el("div", "d-flex gap-3 align-items-center");
    const avatar = el("div", "avatar", item.initials);
    avatar.style.background = item.avatarColor;
    avatar.setAttribute("aria-hidden", "true");
    const info = el("div", "flex-grow-1 min-w-0");
    info.appendChild(el("p", "name text-truncate", item.name));
    const rating = el("div", "meta");
    const st = el("span", "stars", stars(item.rating));
    st.setAttribute("aria-hidden", "true");
    rating.append(st, ` ${item.ratingLabel} (${item.reviewCount} avaliações)`);
    info.appendChild(rating);
    if (item.verified) {
      const v = el("div", "verified");
      v.innerHTML = '<i class="bi bi-patch-check-fill" aria-hidden="true"></i> ';
      v.append("Credencial verificada");
      info.appendChild(v);
    }
    top.append(avatar, info);

    const facts = el("div", "meta mt-2 d-flex flex-wrap gap-x-3 column-gap-3 row-gap-1");
    const f1 = el("span"); f1.innerHTML = '<i class="bi bi-geo-alt" aria-hidden="true"></i> '; f1.append(`${item.distanceLabel} · ${item.neighborhood}`);
    const f2 = el("span"); f2.innerHTML = '<i class="bi bi-mortarboard" aria-hidden="true"></i> '; f2.append(`${item.lessonsGiven} aulas dadas`);
    const f3 = el("span"); f3.innerHTML = '<i class="bi bi-card-checklist" aria-hidden="true"></i> '; f3.append(`Cat. ${item.categories}`);
    facts.append(f1, f2, f3);

    const bottom = el("div", "d-flex justify-content-between align-items-center mt-2");
    const price = el("div");
    price.append(el("span", "price", item.priceLabel), el("span", "meta", " /aula"));
    const link = el("a", "btn btn-primary btn-sm", "Ver perfil");
    link.href = item.url;
    bottom.append(price, link);

    card.append(top, facts, bottom);
    card.addEventListener("click", (e) => { if (e.target !== link) setActive(item.id, { panMap: true }); });
    card.addEventListener("keydown", (e) => { if (e.key === "Enter" && e.target === card) window.location.href = item.url; });
    return card;
  }

  function render(data) {
    markersLayer.clearLayers();
    markers.clear();
    cards.clear();
    cardsEl.replaceChildren();

    const origin = [data.origin.lat, data.origin.lng];
    if (originMarker) originMarker.remove();
    if (radiusCircle) radiusCircle.remove();
    originMarker = L.marker(origin, {
      icon: L.divIcon({ className: "", html: '<div class="you-dot"></div>', iconSize: [16, 16], iconAnchor: [8, 8] }),
      keyboard: false, title: data.origin.label
    }).addTo(map);
    radiusCircle = L.circle(origin, { radius: data.radiusKm * 1000, color: "#157a3b", weight: 1, fillColor: "#22c55e", fillOpacity: 0.05 }).addTo(map);

    data.items.forEach((item) => {
      const m = L.marker([item.lat, item.lng], { icon: priceIcon(item.priceShort, false), priceLabel: item.priceShort, title: item.name, alt: item.name });
      m.on("click", () => setActive(item.id, { scrollCard: true }));
      m.addTo(markersLayer);
      markers.set(item.id, m);
      const card = buildCard(item);
      cards.set(item.id, card);
      cardsEl.appendChild(card);
    });

    const n = data.count;
    titleEl.textContent = n === 0 ? `Nenhum instrutor perto de ${data.origin.label}`
      : `${n} instrutor${n > 1 ? "es" : ""} perto de ${data.origin.label}`;
    emptyEl.classList.toggle("d-none", n !== 0);
    cardsEl.classList.toggle("d-none", n === 0);

    if (n > 0) {
      const bounds = L.latLngBounds(data.items.map((i) => [i.lat, i.lng])).extend(origin);
      map.fitBounds(bounds, { padding: [40, 40], maxZoom: 14 });
    } else {
      map.fitBounds(radiusCircle.getBounds());
    }
  }

  async function search() {
    const params = new URLSearchParams();
    const fd = new FormData(form);
    fd.forEach((v, k) => { if (v !== "") params.set(k, v); });
    if (geo && !params.get("bairro")) { params.set("lat", geo.lat); params.set("lng", geo.lng); }

    errorEl.classList.add("d-none");
    try {
      const res = await fetch(`/api/instrutores?${params.toString()}`, { headers: { Accept: "application/json" }, credentials: "same-origin" });
      if (!res.ok) throw new Error(res.status);
      render(await res.json());
    } catch (err) {
      errorEl.classList.remove("d-none");
    }
  }

  form.addEventListener("submit", (e) => { e.preventDefault(); geo = null; search(); });
  form.querySelectorAll("select").forEach((s) => s.addEventListener("change", search));
  document.getElementById("q").addEventListener("change", () => { geo = null; search(); });

  document.getElementById("locate").addEventListener("click", () => {
    if (!navigator.geolocation) { search(); return; }
    navigator.geolocation.getCurrentPosition(
      (pos) => { geo = { lat: pos.coords.latitude, lng: pos.coords.longitude }; document.getElementById("q").value = ""; search(); },
      () => { geo = null; document.getElementById("q").value = ""; search(); }, // fallback: Av. Paulista
      { enableHighAccuracy: false, timeout: 8000, maximumAge: 60000 }
    );
  });

  search();
})();
