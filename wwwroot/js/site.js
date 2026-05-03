// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(function () {
  function ensureAiLoadingOverlay() {
    let overlay = document.getElementById("aiWorkplanLoadingOverlay");
    if (overlay) return overlay;

    const style = document.createElement("style");
    style.id = "aiWorkplanLoadingStyles";
    style.textContent = `
      .ai-loading-overlay {
        position: fixed;
        inset: 0;
        z-index: 2147483000;
        display: none;
        align-items: center;
        justify-content: center;
        padding: 24px;
        background: rgba(15, 23, 42, .46);
        backdrop-filter: blur(3px);
      }
      .ai-loading-overlay.is-visible { display: flex; }
      .ai-loading-dialog {
        width: min(440px, 100%);
        padding: 24px;
        background: #ffffff;
        border: 1px solid #dbe4ef;
        border-radius: 8px;
        box-shadow: 0 24px 80px rgba(15, 23, 42, .25);
        color: #0f172a;
        text-align: center;
      }
      .ai-loading-spinner {
        width: 44px;
        height: 44px;
        margin: 0 auto 16px;
        border: 4px solid #dbeafe;
        border-top-color: #2563eb;
        border-radius: 50%;
        animation: aiLoadingSpin .85s linear infinite;
      }
      .ai-loading-title {
        margin: 0 0 8px;
        font-size: 1.08rem;
        font-weight: 700;
      }
      .ai-loading-copy {
        margin: 0;
        color: #52647a;
        line-height: 1.5;
      }
      .ai-loading-disclaimer {
        margin: 14px 0 0;
        padding: 10px 12px;
        color: #92400e;
        background: #fffbeb;
        border: 1px solid #fde68a;
        border-radius: 8px;
        font-size: .9rem;
      }
      @keyframes aiLoadingSpin { to { transform: rotate(360deg); } }
      body.ai-pdf-exporting [data-pdf-exclude] { display: none !important; }
    `;
    document.head.appendChild(style);

    overlay = document.createElement("div");
    overlay.id = "aiWorkplanLoadingOverlay";
    overlay.className = "ai-loading-overlay";
    overlay.setAttribute("role", "alertdialog");
    overlay.setAttribute("aria-live", "assertive");
    overlay.setAttribute("aria-modal", "true");
    overlay.innerHTML = `
      <div class="ai-loading-dialog">
        <div class="ai-loading-spinner" aria-hidden="true"></div>
        <h2 class="ai-loading-title">Generando plan con AI</h2>
        <p class="ai-loading-copy">Estamos consultando Microsoft Graph y preparando recomendaciones accionables.</p>
        <p class="ai-loading-disclaimer">Este proceso puede tardar cerca de 30 segundos. No cierres esta ventana mientras se genera el plan.</p>
      </div>
    `;
    document.body.appendChild(overlay);
    return overlay;
  }

  function showAiLoading(trigger) {
    const overlay = ensureAiLoadingOverlay();
    const title = trigger?.getAttribute("data-ai-loading-title");
    const copy = trigger?.getAttribute("data-ai-loading-copy");
    const disclaimer = trigger?.getAttribute("data-ai-loading-disclaimer");

    const titleEl = overlay.querySelector(".ai-loading-title");
    const copyEl = overlay.querySelector(".ai-loading-copy");
    const disclaimerEl = overlay.querySelector(".ai-loading-disclaimer");

    if (titleEl && title) titleEl.textContent = title;
    if (copyEl && copy) copyEl.textContent = copy;
    if (disclaimerEl && disclaimer) disclaimerEl.textContent = disclaimer;

    overlay.classList.add("is-visible");
  }

  document.addEventListener("click", function (event) {
    const trigger = event.target.closest("[data-ai-loading='true']");
    if (!trigger) return;

    if (trigger.tagName === "A") {
      if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
      const href = trigger.getAttribute("href");
      if (!href || href === "#" || href.startsWith("javascript:")) return;

      event.preventDefault();
      showAiLoading(trigger);
      trigger.setAttribute("aria-disabled", "true");
      trigger.classList.add("disabled");
      window.setTimeout(function () {
        window.location.href = href;
      }, 80);
      return;
    }

    showAiLoading(trigger);
  });

  function sanitizeFileName(value) {
    return (value || "plan-ai")
      .normalize("NFD")
      .replace(/[\u0300-\u036f]/g, "")
      .replace(/[^\w.-]+/g, "-")
      .replace(/-+/g, "-")
      .replace(/^-|-$/g, "")
      .toLowerCase();
  }

  async function exportPlanToPdf(trigger) {
    const targetSelector = trigger.getAttribute("data-ai-plan-pdf");
    const target = targetSelector ? document.querySelector(targetSelector) : document.querySelector("[data-ai-plan-export]");
    if (!target) {
      window.alert("No se encontró el contenido del plan para exportar.");
      return;
    }

    if (!window.html2canvas || !window.jspdf || !window.jspdf.jsPDF) {
      window.alert("No se cargaron las librerías necesarias para generar el PDF. Actualiza la página e intenta de nuevo.");
      return;
    }

    const originalHtml = trigger.innerHTML;
    trigger.disabled = true;
    trigger.classList.add("disabled");
    trigger.innerHTML = '<span class="spinner-border spinner-border-sm me-1" aria-hidden="true"></span> Preparando PDF';

    document.body.classList.add("ai-pdf-exporting");

    try {
      await new Promise(resolve => window.setTimeout(resolve, 120));

      const canvas = await window.html2canvas(target, {
        scale: Math.min(window.devicePixelRatio || 1.5, 1.75),
        useCORS: true,
        backgroundColor: "#ffffff",
        width: target.scrollWidth,
        height: target.scrollHeight,
        windowWidth: target.scrollWidth,
        windowHeight: Math.max(target.scrollHeight, window.innerHeight),
        ignoreElements: element => !!element.closest("[data-pdf-exclude]")
      });

      const pdf = new window.jspdf.jsPDF("p", "mm", "a4");
      const pageWidth = pdf.internal.pageSize.getWidth();
      const pageHeight = pdf.internal.pageSize.getHeight();
      const margin = 8;
      const usableWidth = pageWidth - margin * 2;
      const usableHeight = pageHeight - margin * 2;
      const imgData = canvas.toDataURL("image/png");
      const imgHeight = canvas.height * usableWidth / canvas.width;

      let heightLeft = imgHeight;
      let position = margin;

      pdf.addImage(imgData, "PNG", margin, position, usableWidth, imgHeight);
      heightLeft -= usableHeight;

      while (heightLeft > 0) {
        position = margin - (imgHeight - heightLeft);
        pdf.addPage();
        pdf.addImage(imgData, "PNG", margin, position, usableWidth, imgHeight);
        heightLeft -= usableHeight;
      }

      const totalPages = pdf.getNumberOfPages();
      pdf.setFont("helvetica", "normal");
      pdf.setFontSize(8);
      pdf.setTextColor(100, 116, 139);
      for (let page = 1; page <= totalPages; page += 1) {
        pdf.setPage(page);
        pdf.text(`Pagina ${page} de ${totalPages}`, pageWidth - margin, pageHeight - 4, { align: "right" });
      }

      const requestedName = trigger.getAttribute("data-pdf-filename");
      const filename = `${sanitizeFileName(requestedName)}.pdf`;
      pdf.save(filename);
    } catch (error) {
      console.error("No se pudo generar el PDF del plan AI.", error);
      window.alert("No se pudo generar el PDF. Intenta de nuevo en unos segundos.");
    } finally {
      document.body.classList.remove("ai-pdf-exporting");
      trigger.disabled = false;
      trigger.classList.remove("disabled");
      trigger.innerHTML = originalHtml;
    }
  }

  document.addEventListener("click", function (event) {
    const trigger = event.target.closest("[data-ai-plan-pdf]");
    if (!trigger) return;

    event.preventDefault();
    exportPlanToPdf(trigger);
  });
})();
