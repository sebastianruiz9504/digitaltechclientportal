
(function () {

// ===== CONFIGURACIÓN =====

// Endpoint interno del backend que guardará en Dataverse
const DV_ENDPOINT = "/api/assessments"; 

// Escala de madurez (0-5)
const LIKERT = [
  { value: 0, text: "0 — No implementado" },
  { value: 1, text: "1 — Informal" },
  { value: 2, text: "2 — Inicial" },
  { value: 3, text: "3 — Implementado" },
  { value: 4, text: "4 — Medido / monitoreado" },
  { value: 5, text: "5 — Optimizado / mejora continua" }
];

// Bandas de madurez por porcentaje
function bandFromScore(pct) {
  if (pct >= 85) return { label: "Avanzada", cls: "bg-success" };
  if (pct >= 70) return { label: "Intermedia", cls: "bg-primary" };
  if (pct >= 50) return { label: "Básica", cls: "bg-warning text-dark" };
  return { label: "Inicial", cls: "bg-danger" };
}

// Dominios y preguntas (mantenemos tu contenido original)
const SECTIONS = [
  {
    id: "gobierno",
    title: "Gobierno y alcance de la seguridad",
    questions: [
      { id: "scope", type: "likert", text: "¿Tenemos claro y por escrito qué partes de la empresa están cubiertas por nuestro sistema de seguridad? (oficinas, sistemas, procesos y datos)" },
      { id: "policy", type: "likert", text: "¿Contamos con una política de seguridad aprobada por la dirección y conocida por todos?" },
      { id: "roles", type: "likert", text: "¿Está claro quién se encarga de la seguridad y qué funciones tiene cada persona o comité?" },
      { id: "risk_method", type: "likert", text: "¿Tenemos una forma definida de identificar, evaluar y manejar los riesgos que puedan afectar al negocio?" }
    ],
    recs: [
      "Definir y documentar el alcance del sistema de seguridad con límites, dependencias y terceros críticos.",
      "Aprobar y difundir la política de seguridad desde la alta dirección.",
      "Asignar roles claros (responsables de seguridad y dueños de procesos) y establecer un comité.",
      "Adoptar una metodología de riesgos con criterios de impacto/probabilidad y registro vivo."
    ]
  },
  {
    id: "activos",
    title: "Gestión de activos y datos",
    questions: [
      { id: "inventory", type: "likert", text: "¿Tenemos una lista actualizada de equipos, sistemas, aplicaciones, datos y servicios en la nube?" },
      { id: "classification", type: "likert", text: "¿Etiquetamos la información (Público/Interno/Confidencial) y la protegemos según esa etiqueta?" },
      { id: "owners", type: "likert", text: "¿Cada sistema o conjunto de datos tiene un responsable y reglas claras de uso y tiempo de conservación?" }
    ],
    recs: [
      "Completar inventario de activos y datos con propietarios y criticidad.",
      "Aplicar clasificación de la información y controles de protección por clase.",
      "Definir políticas de retención y destrucción segura alineadas a requisitos legales."
    ]
  },
  {
    id: "acceso",
    title: "Control de acceso e identidad",
    questions: [
      { id: "iam", type: "likert", text: "¿Entrar a las aplicaciones clave es sencillo y seguro (inicio único SSO) y con doble verificación para cuentas sensibles?" },
      { id: "least_priv", type: "likert", text: "¿Cada persona tiene solo los permisos que necesita y revisamos accesos con regularidad?" },
      { id: "joiner_mover_leaver", type: "likert", text: "¿Altas, cambios y bajas de personal se aplican a tiempo y de forma automática?" }
    ],
    recs: [
      "Habilitar MFA obligatorio y SSO para apps críticas.",
      "Establecer revisiones trimestrales de accesos y segregación de funciones.",
      "Automatizar altas, cambios y bajas con flujos revisados por RRHH y TI."
    ]
  },
  {
    id: "operaciones",
    title: "Seguridad de operaciones y equipos",
    questions: [
      { id: "edr", type: "likert", text: "¿Los equipos y servidores tienen protección activa (antivirus/EDR), disco cifrado y una configuración segura estándar?" },
      { id: "patching", type: "likert", text: "¿Instalamos actualizaciones con plazos definidos según criticidad (sistema operativo, aplicaciones, firmware)?" },
      { id: "logging", type: "likert", text: "¿Reunimos registros de seguridad en una herramienta central y atendemos alertas con guías claras?" }
    ],
    recs: [
      "Estandarizar configuración segura y cifrado de disco en equipos y servidores.",
      "Implementar ciclo de parchado con ventanas, excepciones justificadas y métricas.",
      "Centralizar logs en SIEM y definir umbrales/alertas con respuesta documentada."
    ]
  },
  {
    id: "redes",
    title: "Seguridad de redes y comunicaciones",
    questions: [
      { id: "segmentation", type: "likert", text: "¿La red está dividida en zonas para limitar el alcance de incidentes (tráfico interno y externo)?" },
      { id: "tls", type: "likert", text: "¿La información viaja cifrada con versiones modernas de TLS y certificados bien gestionados?" },
      { id: "remote", type: "likert", text: "¿El acceso remoto usa VPN o acceso de confianza cero con doble verificación y validación del equipo?" }
    ],
    recs: [
      "Implementar segmentación por zonas y microsegmentación para cargas críticas.",
      "Estandarizar TLS moderno, rotación de certificados y HSTS donde aplique.",
      "Adoptar ZTNA o VPN endurecida con MFA y validaciones de postura del dispositivo."
    ]
  },
  {
    id: "desarrollo",
    title: "Adquisición, desarrollo y cambios",
    questions: [
      { id: "sdlc", type: "likert", text: "¿Incluimos revisiones y pruebas de seguridad automáticas en el ciclo de desarrollo (SAST/DAST y detección de secretos)?" },
      { id: "changes", type: "likert", text: "¿Los cambios pasan por aprobación, pruebas y plan de reversa definidos?" }
    ],
    recs: [
      "Integrar análisis de seguridad en CI/CD (SAST, DAST, composición de software).",
      "Operar un proceso de cambios con evaluación de riesgo, evidencia y plan de retorno."
    ]
  },
  {
    id: "proveedores",
    title: "Proveedores y terceros",
    questions: [
      { id: "due_diligence", type: "likert", text: "¿Evaluamos la seguridad de proveedores antes de contratarlos y dejamos los requisitos en el contrato?" },
      { id: "monitoring_third", type: "likert", text: "¿Hacemos seguimiento de su desempeño y renovaciones con cláusulas de seguridad y niveles de servicio?" }
    ],
    recs: [
      "Establecer onboarding de terceros con evaluación de riesgos y controles requeridos.",
      "Incluir cláusulas de seguridad, derecho a auditoría y SLAs; monitorear renovaciones."
    ]
  },
  {
    id: "incidentes",
    title: "Gestión de incidentes",
    questions: [
      { id: "ir_plan", type: "likert", text: "¿Tenemos un plan claro para incidentes: roles, niveles de severidad y pasos a seguir?" },
      { id: "exercises", type: "likert", text: "¿Practicamos con ejercicios y aplicamos mejoras después de cada simulación o incidente real?" }
    ],
    recs: [
      "Documentar plan de respuesta, catálogo de incidentes y flujo de escalamiento.",
      "Ejecutar ejercicios periódicos y registrar lecciones aprendidas con acciones."
    ]
  },
  {
    id: "continuidad",
    title: "Continuidad de negocio y respaldo",
    questions: [
      {
        id: "backup_coverage",
        type: "multi",
        text: "¿Qué estamos respaldando hoy? (marca lo que corresponda):",
        options: [
          { key: "servers", label: "Servidores (físicos/virtuales)", weight: 1 },
          { key: "endpoints", label: "Equipos locales/endpoints críticos", weight: 1 },
          { key: "m365", label: "Datos M365/Google Workspace (correo, OneDrive/Drive, SharePoint)", weight: 1 }
        ]
      },
      { id: "backup_freq", type: "likert", text: "¿La frecuencia y las reglas de las copias de seguridad coinciden con la importancia del sistema (retención e inmutabilidad)?" },
      { id: "restore_test", type: "likert", text: "¿Probamos restauraciones periódicamente y medimos los tiempos?" },
      { id: "rto_rpo", type: "likert", text: "¿Definimos y validamos RTO (tiempo objetivo de recuperación) y RPO (pérdida aceptable de datos) para sistemas críticos?" }
    ],
    recs: [
      "Asegurar cobertura de respaldos para servidores, endpoints críticos y datos SaaS (M365/Google).",
      "Implementar retenciones por criticidad, copias inmutables y separación lógica (3-2-1).",
      "Probar restauraciones periódicas y registrar tiempos para validar RTO/RPO.",
      "Completar BIA para priorizar procesos y alinear estrategias de recuperación."
    ]
  },
  {
    id: "cumplimiento",
    title: "Cumplimiento y auditoría",
    questions: [
      { id: "awareness", type: "likert", text: "¿Tenemos un programa de concienciación (phishing, políticas) con métricas de participación y efectividad?" },
      { id: "audits", type: "likert", text: "¿Realizamos auditorías internas periódicas y hacemos seguimiento hasta cerrar hallazgos?" }
    ],
    recs: [
      "Operar un plan anual de concienciación con KPIs (tasa de clic, reporte, repetición).",
      "Planificar auditorías internas y seguimiento de no conformidades a cierre."
    ]
  }
];

// ===== Estado en tiempo de ejecución =====
let state = {
  sectionIndex: 0,
  answers: {}, // { [questionId]: value } ; para multi: {backup_coverage: {servers:true, endpoints:false, ...}}
  respNombre: "",
  respEmpresa: ""
};

// ===== Elementos del DOM =====
const root = document.getElementById("assessmentRoot");
if (!root) return;

const progressBar = root.querySelector("#progressBar");
const sectionTitle = root.querySelector("#sectionTitle");
const sectionCounter = root.querySelector("#sectionCounter");
const form = root.querySelector("#questionsForm");
const btnPrev = root.querySelector("#btnPrev");
const btnNext = root.querySelector("#btnNext");
const btnFinish = root.querySelector("#btnFinish");
const summaryPanel = root.querySelector("#summaryPanel");
const overallScoreEl = root.querySelector("#overallScore");
const overallBandEl = root.querySelector("#overallBand");
const overallProgressEl = root.querySelector("#overallProgress");
const domainScoresEl = root.querySelector("#domainScores");
const recsEl = root.querySelector("#recommendations");

// Nuevos: campos de responsable
const respNombreEl = root.querySelector("#respNombre");
const respEmpresaEl = root.querySelector("#respEmpresa");

// ===== Navegación =====
btnPrev.addEventListener("click", () => {
  if (state.sectionIndex > 0) {
    state.sectionIndex--;
    renderSection();
  }
});

btnNext.addEventListener("click", () => {
  if (!validateSection()) return;
  if (state.sectionIndex < SECTIONS.length - 1) {
    state.sectionIndex++;
    renderSection();
  }
});

btnFinish.addEventListener("click", async () => {
  if (!validateSection()) return;

  // Validar datos del responsable
  state.respNombre = (respNombreEl?.value || "").trim();
  state.respEmpresa = (respEmpresaEl?.value || "").trim();

  if (!state.respNombre || !state.respEmpresa) {
    respNombreEl?.classList.add("is-invalid");
    respEmpresaEl?.classList.add("is-invalid");
    (respNombreEl || respEmpresaEl)?.scrollIntoView({ behavior: "smooth", block: "center" });
    return;
  } else {
    respNombreEl?.classList.remove("is-invalid");
    respEmpresaEl?.classList.remove("is-invalid");
  }

  // Construir resumen y estado
  showSummary();

  // Generar PDF para descarga local
  try {
    const pdfBlob = await buildPdfBlob(state);
    // Descarga manual no bloqueante (opcional: dejar el botón de exportación)
    // Aquí NO enviamos el PDF a Dataverse (tu tabla tiene 3 columnas).
    // Enviamos solo el encapsulado JSON de respuestas y puntajes.
    
    // Payload compacto para Dataverse
    const payload = {
      cliente: state.respEmpresa,
      nombre: state.respNombre,
      respuestas: buildCompactAnswers(state)
    };

    // Guardar en backend → Dataverse
    await fetch(DV_ENDPOINT, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });
  } catch (err) {
    console.error("Error guardando assessment:", err);
    // No bloqueamos la experiencia del usuario si falla; el resumen ya se mostró
  }
});

// Exportar (descarga manual, opcional)
document.addEventListener("click", async (e) => {
  if (e.target && e.target.id === "btnExportPdf") {
    const blob = await buildPdfBlob(state);
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = buildFileName(state);
    a.click();
    URL.revokeObjectURL(a.href);
  }
});

// Render inicial
renderSection();

function renderSection() {
  const total = SECTIONS.length;
  const idx = state.sectionIndex;
  const section = SECTIONS[idx];

  // Títulos y progreso
  sectionTitle.textContent = section.title;
  sectionCounter.textContent = `Sección ${idx + 1} de ${total}`;
  const pct = Math.round((idx / total) * 100);
  progressBar.style.width = `${pct}%`;

  // Botones
  btnPrev.disabled = idx === 0;
  btnNext.classList.toggle("d-none", idx === total - 1);
  btnFinish.classList.toggle("d-none", idx !== total - 1);

  // Preguntas
  form.innerHTML = "";
  section.questions.forEach((q, i) => {
    const card = document.createElement("div");
    card.className = "card mb-3";
    const body = document.createElement("div");
    body.className = "card-body";
    const label = document.createElement("label");
    label.className = "form-label fw-semibold";
    label.textContent = q.text;
    body.appendChild(label);

    if (q.type === "likert") {
      const group = document.createElement("div");
      group.className = "d-flex flex-column gap-1 mt-2";
      LIKERT.forEach(opt => {
        const id = `${section.id}_${q.id}_${opt.value}`;
        const wrap = document.createElement("div");
        wrap.className = "form-check";
        const input = document.createElement("input");
        input.className = "form-check-input";
        input.type = "radio";
        input.name = `${section.id}_${q.id}`;
        input.id = id;
        input.value = String(opt.value);
        const prev = getAnswer(q.id);
        if (typeof prev === "number" && prev === opt.value) input.checked = true;
        input.addEventListener("change", () => setAnswer(q.id, opt.value));
        const lbl = document.createElement("label");
        lbl.className = "form-check-label likert-label";
        lbl.htmlFor = id;
        lbl.textContent = opt.text;
        wrap.appendChild(input);
        wrap.appendChild(lbl);
        group.appendChild(wrap);
      });
      body.appendChild(group);
    } else if (q.type === "multi") {
      const group = document.createElement("div");
      group.className = "d-flex flex-column gap-1 mt-2";
      const prev = getAnswer(q.id) || {};
      q.options.forEach(opt => {
        const id = `${section.id}_${q.id}_${opt.key}`;
        const wrap = document.createElement("div");
        wrap.className = "form-check";
        const input = document.createElement("input");
        input.className = "form-check-input";
        input.type = "checkbox";
        input.id = id;
        input.checked = !!prev[opt.key];
        input.addEventListener("change", () => {
          const current = getAnswer(q.id) || {};
          current[opt.key] = input.checked;
          setAnswer(q.id, current);
        });
        const lbl = document.createElement("label");
        lbl.className = "form-check-label";
        lbl.htmlFor = id;
        lbl.textContent = opt.label;
        wrap.appendChild(input);
        wrap.appendChild(lbl);
        group.appendChild(wrap);
      });
      body.appendChild(group);
    }

    card.appendChild(body);
    form.appendChild(card);
  });
}

function validateSection() {
  const section = SECTIONS[state.sectionIndex];
  let valid = true;
  section.questions.forEach((q, i) => {
    const card = form.querySelector(`.card:nth-child(${i + 1})`);
    if (q.type === "likert") {
      const val = getAnswer(q.id);
      if (typeof val !== "number") {
        valid = false;
        card.classList.add("border-danger");
      } else {
        card.classList.remove("border-danger");
      }
    } else if (q.type === "multi") {
      const val = getAnswer(q.id) || {};
      const any = Object.values(val).some(Boolean);
      card.classList.toggle("border-warning", !any);
    }
  });

  if (!valid) {
    const firstInvalid = form.querySelector(".border-danger");
    if (firstInvalid) firstInvalid.scrollIntoView({ behavior: "smooth", block: "center" });
  }
  return valid;
}

function getAnswer(qid) { return state.answers[qid]; }
function setAnswer(qid, value) { state.answers[qid] = value; }

// ===== Resumen de resultados y preparación de estado para PDF =====
function showSummary() {
  // Puntaje por dominio
  const domainScores = SECTIONS.map(sec => {
    let sum = 0, count = 0;
    sec.questions.forEach(q => {
      if (q.type === "likert") {
        const v = getAnswer(q.id);
        if (typeof v === "number") { sum += v; count += 1; }
      } else if (q.type === "multi") {
        const sel = getAnswer(q.id) || {};
        const selected = Object.values(sel).filter(Boolean).length;
        const max = q.options.length;
        const normalized = max === 0 ? 0 : (selected / max) * 5;
        sum += normalized; count += 1;
      }
    });
    const avg0to5 = count ? (sum / count) : 0;
    const pct = Math.round((avg0to5 / 5) * 100);
    return { id: sec.id, title: sec.title, pct, avg0to5 };
  });

  const overallPct = Math.round(domainScores.reduce((a, d) => a + d.pct, 0) / domainScores.length);
  const band = bandFromScore(overallPct);

  // Recomendaciones priorizadas
  const recItems = [];
  domainScores
    .sort((a, b) => a.pct - b.pct)
    .forEach(d => {
      const sec = SECTIONS.find(s => s.id === d.id);
      if (!sec?.recs) return;
      if (d.pct < 40) {
        sec.recs.forEach(r => recItems.push({ text: `[CRÍTICO] ${r}`, priority: 1, domain: d.title }));
      } else if (d.pct < 60) {
        sec.recs.forEach(r => recItems.push({ text: r, priority: 2, domain: d.title }));
      }
    });

  if (recItems.length === 0) {
    recItems.push(
      { text: "Mantener ciclo de mejora continua (KPIs de controles, auditorías internas y revisión de dirección).", priority: 3, domain: "General" },
      { text: "Ejecutar ejercicios de crisis y pruebas de restauración para validar RTO/RPO al menos semestralmente.", priority: 3, domain: "Continuidad" }
    );
  }

  // Guardar en estado para export
  state.overallPct = overallPct;
  state.overallBand = band; // { label, cls }
  state.domainScores = domainScores; // [{id,title,pct,avg0to5}]
  state.recItems = recItems; // [{text,priority,domain}]

  // UI resumen
  overallScoreEl.textContent = `${overallPct}%`;
  overallBandEl.textContent = band.label;
  overallBandEl.className = `badge ${band.cls}`;
  overallProgressEl.style.width = `${overallPct}%`;

  domainScoresEl.innerHTML = "";
  domainScores.forEach(d => {
    const bandD = bandFromScore(d.pct);
    const row = document.createElement("div");
    row.className = "domain-row mb-2";
    row.innerHTML = `
      <div class="d-flex align-items-center justify-content-between">
        <div class="fw-semibold">${d.title}</div>
        <div class="text-muted">${d.pct}%</div>
      </div>
      <div class="progress">
        <div class="progress-bar ${bandD.cls}" style="width: ${d.pct}%"></div>
      </div>
    `;
    domainScoresEl.appendChild(row);
  });

  recsEl.innerHTML = "";
  state.recItems
    .sort((a, b) => a.priority - b.priority)
    .forEach(item => {
      const li = document.createElement("li");
      li.className = "list-group-item";
      li.innerHTML = `<span class="badge bg-light text-dark me-2">${item.domain}</span>${item.text}`;
      recsEl.appendChild(li);
    });

  // Mostrar resumen y ajustar navegación
  summaryPanel.classList.remove("d-none");
  sectionTitle.textContent = "Resultados del assessment";
  sectionCounter.textContent = "";
  form.innerHTML = "";
  btnPrev.classList.add("d-none");
  btnNext.classList.add("d-none");
  btnFinish.classList.add("d-none");
  progressBar.style.width = "100%";
}

// ===== Compactación para Dataverse =====
function buildCompactAnswers(state) {
  // JSON compacto: puntaje global, banda, puntajes por dominio y respuestas crudas
  const answers = {};
  for (const sec of SECTIONS) {
    for (const q of sec.questions) {
      answers[q.id] = state.answers[q.id] ?? null;
    }
  }

  return {
    overallPct: state.overallPct,
    overallBand: state.overallBand?.label,
    domains: (state.domainScores || []).map(d => ({ id: d.id, title: d.title, pct: d.pct })),
    recommendations: (state.recItems || []).map(r => ({ domain: r.domain, text: r.text, priority: r.priority })),
    answers
  };
}

// ===== PDF: construcción como Blob (para enviar y/o descargar) =====
function buildFileName(state) {
  const name = (state.respNombre || "responsable").replace(/\s+/g, "_");
  const empresa = (state.respEmpresa || "empresa").replace(/\s+/g, "_");
  return `assessment-seguridad-${empresa}-${name}.pdf`;
}

async function buildPdfBlob(state) {
  const { jsPDF } = window.jspdf;
  const pdf = new jsPDF("p", "mm", "a4");
  const pageWidth = pdf.internal.pageSize.getWidth();

  injectPdfStyles();
  const container = buildExportContainer();
  document.body.appendChild(container);

  const pages = [];
  pages.push(buildCoverPage(state)); // Portada
  pages.push(buildDomainsPage(state));
  pages.push(buildRecommendationsPage(state));

  for (let i = 0; i < pages.length; i++) {
    const pageEl = pages[i];
    const canvas = await html2canvas(pageEl, {
      scale: 2,
      useCORS: true,
      width: pageEl.clientWidth,
      height: pageEl.clientHeight,
      windowWidth: pageEl.clientWidth,
      backgroundColor: "#ffffff"
    });
    const imgData = canvas.toDataURL("image/png");
    const imgProps = pdf.getImageProperties(imgData);
    const imgH = (imgProps.height * pageWidth) / imgProps.width;

    if (i === 0) {
      pdf.addImage(imgData, "PNG", 0, 0, pageWidth, imgH);
    } else {
      pdf.addPage();
      pdf.addImage(imgData, "PNG", 0, 0, pageWidth, imgH);
    }
  }

  // Numeración
  const total = pdf.getNumberOfPages();
  const pageHeight = pdf.internal.pageSize.getHeight();
  pdf.setFont("helvetica", "normal");
  pdf.setFontSize(8);
  for (let i = 1; i <= total; i++) {
    pdf.setPage(i);
    pdf.text(`Página ${i} de ${total}`, pageWidth - 12, pageHeight - 8, { align: "right" });
  }

  const blob = pdf.output("blob");
  container.remove();
  return blob;
}

function blobToBase64(blob) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onloadend = () => {
      const res = reader.result;
      if (typeof res === "string") {
        const base64 = res.split(",")[1] || res;
        resolve(base64);
      } else {
        reject(new Error("No se pudo convertir Blob a Base64"));
      }
    };
    reader.onerror = reject;
    reader.readAsDataURL(blob);
  });
}

// ===== Helpers de maquetación PDF =====
function injectPdfStyles() {
  if (document.getElementById("pdfExportStyles")) return;
  const style = document.createElement("style");
  style.id = "pdfExportStyles";
  style.textContent = `
  #pdfExportContainer { position: fixed; left: -99999px; top: 0; width: 794px; z-index: -1; }
  .pdf-page { width: 794px; min-height: 1123px; box-sizing: border-box; padding: 40px 48px; background: #fff; color: #0f172a; font-family: system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, "Noto Sans"; }
  .pdf-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 16px; padding-bottom: 12px; border-bottom: 1px solid #e5e7eb; font-size: 12px; color: #475569; }
  .pdf-title { font-size: 22px; font-weight: 600; color: #0f172a; margin: 8px 0 4px; }
  .pdf-subtitle { font-size: 14px; color: #475569; margin: 0; }
  .meta { font-size: 13px; color:#475569; margin-top:4px; }
  .kpi { display: flex; gap: 24px; align-items: center; margin: 24px 0; }
  .kpi .score { font-size: 48px; line-height: 1; font-weight: 700; color: #0f172a; }
  .badge { display: inline-block; padding: 4px 8px; border-radius: 999px; font-size: 12px; font-weight: 600; }
  .bg-success { background: #16a34a; color: #fff; }
  .bg-primary { background: #2563eb; color: #fff; }
  .bg-warning { background: #f59e0b; color: #111827; }
  .bg-danger { background: #dc2626; color: #fff; }
  .progress-row { margin: 10px 0; }
  .progress-label { display:flex; justify-content:space-between; font-size: 12px; color:#475569; margin-bottom:6px; }
  .progress { height: 8px; background: #e5e7eb; border-radius: 999px; overflow: hidden; }
  .progress-bar { height: 100%; background: #16a34a; }
  .table { width: 100%; border-collapse: collapse; margin-top: 8px; }
  .table th, .table td { padding: 8px 10px; border-bottom: 1px solid #e5e7eb; font-size: 12px; text-align: left; }
  .table th { font-weight: 600; color: #0f172a; }
  .list { margin: 0; padding-left: 18px; }
  .list li { margin-bottom: 8px; font-size: 12px; color: #0f172a; }
  .section { margin-top: 18px; }
  .section h3 { font-size: 16px; font-weight: 600; margin: 0 0 8px; color: #0f172a; }
  `;
  document.head.appendChild(style);
}

function buildExportContainer() {
  const c = document.createElement("div");
  c.id = "pdfExportContainer";
  return c;
}

function buildHeader(container, title) {
  const hdr = document.createElement("div");
  hdr.className = "pdf-header";
  const left = document.createElement("div");
  left.textContent = title;
  const right = document.createElement("div");
  right.textContent = new Date().toLocaleDateString("es-CO");
  hdr.appendChild(left);
  hdr.appendChild(right);
  container.appendChild(hdr);
}

function buildCoverPage(state) {
  const page = document.createElement("div");
  page.className = "pdf-page";
  buildHeader(page, "Assessment de Seguridad y Continuidad");
  const h1 = document.createElement("h2");
  h1.className = "pdf-title";
  h1.textContent = "Resumen de resultados";
  page.appendChild(h1);
  const p = document.createElement("p");
  p.className = "pdf-subtitle";
  p.textContent = "Puntaje global y estado general del programa de seguridad.";
  page.appendChild(p);

  // Datos del responsable
  const meta = document.createElement("div");
  meta.className = "meta";
  meta.innerHTML = `<strong>Responsable:</strong> ${state.respNombre || "—"} &nbsp; | &nbsp; <strong>Empresa:</strong> ${state.respEmpresa || "—"}`;
  page.appendChild(meta);

  const kpi = document.createElement("div");
  kpi.className = "kpi";
  const score = document.createElement("div");
  score.className = "score";
  score.textContent = `${state.overallPct ?? 0}%`;
  const band = document.createElement("div");
  band.innerHTML = `<span class="badge ${state.overallBand?.cls || "bg-danger"}">${state.overallBand?.label || "Inicial"}</span>`;
  kpi.appendChild(score);
  kpi.appendChild(band);
  page.appendChild(kpi);

  // Barra global
  const row = document.createElement("div");
  row.className = "progress-row";
  const lbl = document.createElement("div");
  lbl.className = "progress-label";
  lbl.innerHTML = `<span>Puntaje global</span><span>${state.overallPct ?? 0}%</span>`;
  const bar = document.createElement("div");
  bar.className = "progress";
  const barIn = document.createElement("div");
  barIn.className = "progress-bar";
  barIn.style.width = `${state.overallPct ?? 0}%`;
  bar.appendChild(barIn);
  row.appendChild(lbl);
  row.appendChild(bar);
  page.appendChild(row);

  // Top 3 prioridades
  if (Array.isArray(state.recItems) && state.recItems.length) {
    const sec = document.createElement("div");
    sec.className = "section";
    const h3 = document.createElement("h3");
    h3.textContent = "Prioridades inmediatas";
    sec.appendChild(h3);
    const ul = document.createElement("ul");
    ul.className = "list";
    state.recItems.slice(0, 3).forEach(r => {
      const li = document.createElement("li");
      li.textContent = r.text.replace(/^\[CRÍTICO\]\s*/, '');
      ul.appendChild(li);
    });
    sec.appendChild(ul);
    page.appendChild(sec);
  }

  document.getElementById("pdfExportContainer").appendChild(page);
  return page;
}

function buildDomainsPage(state) {
  const page = document.createElement("div");
  page.className = "pdf-page";
  buildHeader(page, "Assessment de Seguridad y Continuidad");
  const h1 = document.createElement("h2");
  h1.className = "pdf-title";
  h1.textContent = "Puntaje por dominio";
  page.appendChild(h1);

  const table = document.createElement("table");
  table.className = "table";
  table.innerHTML = `
    <thead>
      <tr><th>Dominio</th><th style="width: 160px;">Puntaje</th></tr>
    </thead>
    <tbody></tbody>
  `;
  const tbody = table.querySelector("tbody");

  (state.domainScores || []).forEach(d => {
    const tr = document.createElement("tr");
    const tdDom = document.createElement("td");
    tdDom.textContent = d.title;
    const tdPct = document.createElement("td");
    const wrap = document.createElement("div");
    wrap.className = "progress-row";
    const label = document.createElement("div");
    label.className = "progress-label";
    label.innerHTML = `<span></span><span>${d.pct}%</span>`;
    const bar = document.createElement("div");
    bar.className = "progress";
    const barIn = document.createElement("div");
    barIn.className = "progress-bar";
    const cls = bandFromScore(d.pct).cls;
    barIn.style.background =
      cls.includes("success") ? "#16a34a" :
      cls.includes("primary") ? "#2563eb" :
      cls.includes("warning") ? "#f59e0b" : "#dc2626";
    barIn.style.width = `${d.pct}%`;
    bar.appendChild(barIn);
    wrap.appendChild(label);
    wrap.appendChild(bar);
    tdPct.appendChild(wrap);
    tr.appendChild(tdDom);
    tr.appendChild(tdPct);
    tbody.appendChild(tr);
  });

  page.appendChild(table);
  document.getElementById("pdfExportContainer").appendChild(page);
  return page;
}

function buildRecommendationsPage(state) {
  const page = document.createElement("div");
  page.className = "pdf-page";
  buildHeader(page, "Assessment de Seguridad y Continuidad");
  const h1 = document.createElement("h2");
  h1.className = "pdf-title";
  h1.textContent = "Recomendaciones priorizadas";
  page.appendChild(h1);
  const sub = document.createElement("p");
  sub.className = "pdf-subtitle";
  sub.textContent = "Acciones sugeridas según el puntaje de cada dominio. Primero las críticas, luego mejoras y mantenimiento.";
  page.appendChild(sub);
  const list = document.createElement("ul");
  list.className = "list";
  (state.recItems || [])
    .sort((a, b) => a.priority - b.priority)
    .forEach(item => {
      const li = document.createElement("li");
      const tag = item.priority === 1 ? "CRÍTICO" : item.priority === 2 ? "MEJORA" : "MANTENER";
      li.innerHTML = `<strong>[${tag}]</strong> (${item.domain}) — ${item.text.replace(/^\[CRÍTICO\]\s*/,'')}`;
      list.appendChild(li);
    });
  page.appendChild(list);
  document.getElementById("pdfExportContainer").appendChild(page);
  return page;
}

})();
