// Dirija! — checkout: timer do bloqueio temporário, troca de forma de pagamento e "copia e cola".
(function () {
  "use strict";

  const timer = document.getElementById("timer");
  let seconds = parseInt(timer.dataset.seconds, 10) || 0;

  function tick() {
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    timer.textContent = `${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
    timer.classList.toggle("expiring", seconds <= 60);
    if (seconds <= 0) {
      document.getElementById("hold-banner").classList.add("d-none");
      document.getElementById("expired").classList.remove("d-none");
      document.querySelectorAll(".pay-submit").forEach((b) => (b.disabled = true));
      return;
    }
    seconds--;
    setTimeout(tick, 1000);
  }
  tick();

  const pix = document.getElementById("panel-pix");
  const card = document.getElementById("panel-card");
  document.querySelectorAll('input[name="Method"]').forEach((r) =>
    r.addEventListener("change", () => {
      const isCard = document.getElementById("m-card").checked;
      pix.classList.toggle("d-none", isCard);
      card.classList.toggle("d-none", !isCard);
    })
  );

  const number = document.getElementById("CardNumber");
  number.addEventListener("input", () => {
    const digits = number.value.replace(/\D/g, "").slice(0, 19);
    number.value = digits.replace(/(.{4})/g, "$1 ").trim();
  });
  const expiry = document.getElementById("CardExpiry");
  expiry.addEventListener("input", () => {
    const d = expiry.value.replace(/\D/g, "").slice(0, 4);
    expiry.value = d.length > 2 ? `${d.slice(0, 2)}/${d.slice(2)}` : d;
  });

  const copyBtn = document.getElementById("copy-pix");
  copyBtn.addEventListener("click", async () => {
    const input = document.getElementById("pix-code");
    try {
      await navigator.clipboard.writeText(input.value);
    } catch {
      input.select();
      document.execCommand("copy");
    }
    copyBtn.innerHTML = '<i class="bi bi-check2" aria-hidden="true"></i> Copiado';
  });

  // Avoid double submit.
  document.getElementById("pay-form").addEventListener("submit", () => {
    setTimeout(() => document.querySelectorAll(".pay-submit").forEach((b) => (b.disabled = true)), 0);
  });
})();
