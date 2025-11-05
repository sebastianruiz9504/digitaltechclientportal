// wwwroot/js/Capacitaciones-calendar.js
(function () {
    // Exportamos una función global para inicializar la vista dentro de un contenedor inyectado
    window.initDisponibilidad = async function (root) {
        // Permite pasar un selector o un elemento
        const rootEl = (typeof root === "string") ? document.querySelector(root) : root;
        if (!rootEl) {
            console.error("initDisponibilidad: contenedor no encontrado");
            return;
        }

        // Referencias dentro del contenedor
        const headerDias = rootEl.querySelector("#headerDias");
        const bodyHoras = rootEl.querySelector("#bodyHoras");
        const weekLabel = rootEl.querySelector("#weekLabel");

        const modalEl = rootEl.querySelector("#reservaModal");
        const toastEl = rootEl.querySelector("#successToast");

        const btnPrev = rootEl.querySelector("#btnPrev");
        const btnNext = rootEl.querySelector("#btnNext");
        const btnToday = rootEl.querySelector("#btnToday");
        const btnConfirmar = rootEl.querySelector("#btnConfirmarReserva");
        const temaSelect = rootEl.querySelector("#temaSelect");
        const obsInput = rootEl.querySelector("#observaciones");
        const reservaHoraEl = rootEl.querySelector("#reservaHora");

        if (!headerDias || !bodyHoras || !modalEl || !btnPrev || !btnNext || !btnToday || !btnConfirmar) {
            console.error("initDisponibilidad: faltan elementos requeridos en el HTML inyectado");
            return;
        }

        // Instanciadores de Bootstrap ligados a este root
        const getModal = () => new bootstrap.Modal(modalEl);
        const showToast = () => new bootstrap.Toast(toastEl).show();

        // Estado local
        let allSlots = [];
        let currentWeekStart = startOfWeek(new Date());
        let slotSeleccionado = null;

        // Horas visibles (8:00 a 15:00, bloques de 1h)
        const horas = Array.from({ length: 8 }, (_, i) => `${String(8 + i).padStart(2, "0")}:00`);

        // Navegación
        btnPrev.addEventListener("click", () => {
            currentWeekStart = addDays(currentWeekStart, -7);
            renderWeek();
        });
        btnNext.addEventListener("click", () => {
            currentWeekStart = addDays(currentWeekStart, 7);
            renderWeek();
        });
        btnToday.addEventListener("click", () => {
            currentWeekStart = startOfWeek(new Date());
            renderWeek();
        });

        // Confirmar reserva
        btnConfirmar.addEventListener("click", async () => {
            const tema = temaSelect?.value ?? "";
            const observaciones = (obsInput?.value ?? "").trim();

            if (!slotSeleccionado) return;
            if (!tema) {
                alert("Por favor selecciona un tema antes de confirmar.");
                return;
            }

            btnConfirmar.disabled = true;
            btnConfirmar.innerText = "Reservando...";

            try {
                const resp = await fetch("/api/calendarioapi/reservar", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ horaInicio: slotSeleccionado, tema, observaciones })
                });

                if (resp.ok) {
                    bootstrap.Modal.getInstance(modalEl).hide();
                    clearModalInputs();
                    slotSeleccionado = null;
                    await cargarDatos(); // refresca
                    if (toastEl) showToast();
                } else {
                    alert("Error al reservar. Inténtalo nuevamente.");
                }
            } catch (e) {
                console.error(e);
                alert("Error de conexión.");
            } finally {
                btnConfirmar.disabled = false;
                btnConfirmar.innerText = "Confirmar";
            }
        });

        // Limpia al cerrar modal
        modalEl.addEventListener("hidden.bs.modal", () => {
            clearModalInputs();
            slotSeleccionado = null;
        });

        // Carga inicial
        await cargarDatos();

        // ----- Helpers de flujo (scoped al root) -----
        function clearModalInputs() {
            if (temaSelect) temaSelect.value = "";
            if (obsInput) obsInput.value = "";
            if (reservaHoraEl) reservaHoraEl.textContent = "";
        }

        async function cargarDatos() {
            try {
                const resp = await fetch("/api/calendarioapi/disponibilidad");
                if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
                allSlots = await resp.json();
                renderWeek();
            } catch (err) {
                console.error("Error cargando disponibilidad:", err);
                if (bodyHoras) {
                    bodyHoras.innerHTML = `<tr><td colspan="6">
                        <div class="alert alert-danger">No se pudo cargar la disponibilidad.</div>
                    </td></tr>`;
                }
            }
        }

        function renderWeek() {
            // Construye lunes a viernes
            const days = [];
            let d = new Date(currentWeekStart);
            for (let i = 0; i < 5; i++) {
                days.push(new Date(d));
                d = addDays(d, 1);
            }

            // Cabecera
            headerDias.innerHTML = `<th>Hora</th>`;
            days.forEach(day => {
                headerDias.innerHTML += `<th>${day.toLocaleDateString("es-CO", {
                    weekday: "short", day: "2-digit", month: "short"
                })}</th>`;
            });

            // Rango visible
            if (weekLabel) {
                weekLabel.textContent = `${formatDayMonth(currentWeekStart)} - ${formatDayMonth(days[days.length - 1])}`;
            }

            // Índice rápido YYYY-MM-DD|HH:mm
            const index = new Map();
            for (const s of allSlots) {
                const start = new Date(s.horaInicio);
                const key = `${ymdLocal(start)}|${hhmmLocal(start)}`;
                index.set(key, s);
            }

            // Cuerpo
            bodyHoras.innerHTML = "";
            for (const hora of horas) {
                let row = `<tr><td class="fw-bold">${hora}</td>`;
                for (const day of days) {
                    const key = `${ymdLocal(day)}|${hora}`;
                    const slot = index.get(key);

                    if (!slot) {
                        row += `<td class="vacio">—</td>`;
                    } else if (slot.disponible) {
                        row += `<td class="disponible" data-inicio="${slot.horaInicio}">Disponible</td>`;
                    } else {
                        row += `<td class="ocupado">Ocupado</td>`;
                    }
                }
                row += "</tr>";
                bodyHoras.insertAdjacentHTML("beforeend", row);
            }

            // Click en celdas disponibles
            rootEl.querySelectorAll("td.disponible").forEach(td => {
                td.addEventListener("click", () => {
                    slotSeleccionado = td.dataset.inicio;
                    if (reservaHoraEl) {
                        reservaHoraEl.textContent = new Date(slotSeleccionado).toLocaleString("es-CO", {
                            weekday: "long", day: "2-digit", month: "short",
                            hour: "2-digit", minute: "2-digit"
                        });
                    }
                    getModal().show();
                });
            });
        }

        // ----- Utilidades de fecha -----
        function startOfWeek(date) {
            const d = new Date(date.getFullYear(), date.getMonth(), date.getDate());
            const day = d.getDay(); // 0=dom .. 6=sáb
            const diff = (day === 0 ? -6 : 1) - day; // llevar a lunes
            d.setDate(d.getDate() + diff);
            return d;
        }

        function addDays(date, days) {
            const d = new Date(date);
            d.setDate(d.getDate() + days);
            return d;
        }

        function ymdLocal(date) {
            const y = date.getFullYear();
            const m = String(date.getMonth() + 1).padStart(2, "0");
            const d = String(date.getDate()).padStart(2, "0");
            return `${y}-${m}-${d}`;
        }

        function hhmmLocal(date) {
            const h = String(date.getHours()).padStart(2, "0");
            const m = String(date.getMinutes()).padStart(2, "0");
            return `${h}:${m}`;
        }

        function formatDayMonth(date) {
            return date.toLocaleDateString("es-CO", { day: "2-digit", month: "short" });
        }
    };
})();