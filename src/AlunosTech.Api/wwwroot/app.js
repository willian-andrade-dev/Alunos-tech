"use strict";

const el = (id) => document.getElementById(id);
const statuses = {
  Aberto: { label: "Aberto", className: "open" },
  EmAtendimento: { label: "Em atendimento", className: "progress" },
  Resolvido: { label: "Resolvido", className: "resolved" },
};
const eventNames = {
  Abertura: "Chamado aberto",
  Atendimento: "Atendimento iniciado",
  Atualizacao: "Atualização registrada",
  Resolucao: "Chamado resolvido",
};
const state = {
  token: null,
  user: null,
  tickets: [],
  selected: null,
  history: false,
  busy: false,
  loading: false,
  generation: 0,
  detailRequest: 0,
  listRequest: 0,
};

function node(tag, text, className) {
  const result = document.createElement(tag);
  if (text !== undefined) result.textContent = text;
  if (className) result.className = className;
  return result;
}

function notify(text = "", error = false) {
  el("message").textContent = text;
  el("message").classList.toggle("error", error);
  el("message").hidden = !text;
}

function badge(target, status) {
  const info = statuses[status];
  target.className = "badge " + (info?.className ?? "");
  target.textContent = info?.label ?? status;
}

function shortId(id) {
  return "#" + id.slice(0, 8).toUpperCase();
}

function signOut(message = "") {
  state.generation++;
  state.detailRequest++;
  state.listRequest++;
  state.token = null;
  state.user = null;
  state.tickets = [];
  state.selected = null;
  state.busy = false;
  state.loading = false;
  el("new-dialog").close();
  el("new-form").reset();
  el("attendance-form").reset();
  el("workspace").hidden = true;
  el("login-screen").hidden = false;
  el("password").value = "";
  el("password").type = "password";
  el("toggle-password").textContent = "Mostrar";
  el("toggle-password").setAttribute("aria-pressed", "false");
  el("login-error").textContent = message;
  el("login-error").hidden = !message;
  document.title = "Alunos Tech · Central de suporte";
  el("email").focus();
}

async function request(path, { method = "GET", body, anonymous = false } = {}) {
  const generation = state.generation;
  const headers = { Accept: "application/json" };
  if (!anonymous && state.token)
    headers.Authorization = "Bearer " + state.token;
  if (body !== undefined) headers["Content-Type"] = "application/json";
  let response;
  try {
    response = await fetch("/api" + path, {
      method,
      headers,
      cache: "no-store",
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new Error(
      "Não foi possível conectar. Confira se o sistema está em execução.",
    );
  }
  if (generation !== state.generation)
    throw new Error("A sessão foi encerrada.");
  const contentType = response.headers.get("content-type") ?? "";
  const data = contentType.includes("application/json")
    ? await response.json()
    : null;
  if (generation !== state.generation)
    throw new Error("A sessão foi encerrada.");
  if (response.status === 401 && !anonymous) {
    signOut("Sua sessão terminou. Entre novamente para continuar.");
    throw new Error("Entre novamente para continuar.");
  }
  if (!response.ok) {
    const fallback = {
      401: "E-mail ou senha inválidos.",
      403: "Sua conta não tem permissão para esta ação.",
      404: "O chamado não foi encontrado.",
      409: "O chamado foi alterado. Atualize os detalhes para continuar.",
      429: "Muitas tentativas de acesso. Aguarde um minuto e tente novamente.",
    };
    throw new Error(
      data?.erro ??
        fallback[response.status] ??
        "Não foi possível concluir a operação.",
    );
  }
  return data;
}

function renderTickets() {
  const search = el("search").value.trim().toLocaleLowerCase("pt-BR");
  const status = el("status-filter").value;
  const tickets = state.tickets.filter((ticket) => {
    const matchesView = !state.history || ticket.status === "Resolvido";
    const matchesStatus = !status || ticket.status === status;
    const text = [ticket.titulo, ticket.categoria, ticket.id]
      .join(" ")
      .toLocaleLowerCase("pt-BR");
    return (
      matchesView &&
      matchesStatus &&
      (!search || text.includes(search.replace(/^#/, "")))
    );
  });
  const rows = tickets.map((ticket) => {
    const row = node("tr");
    row.classList.toggle("selected", ticket.id === state.selected?.id);
    const title = node("td");
    title.append(
      node("strong", ticket.titulo),
      node("small", shortId(ticket.id)),
    );
    const category = node("td", ticket.categoria);
    const statusCell = node("td");
    const statusBadge = node("span");
    badge(statusBadge, ticket.status);
    statusCell.append(statusBadge);
    const actions = node("td");
    const open = node("button", "Ver", "text-button");
    open.type = "button";
    open.disabled = state.busy;
    open.setAttribute(
      "aria-label",
      "Ver chamado " + shortId(ticket.id) + ": " + ticket.titulo,
    );
    open.addEventListener("click", () => openTicket(ticket.id));
    actions.append(open);
    row.append(title, category, statusCell, actions);
    return row;
  });
  el("ticket-rows").replaceChildren(...rows);
  el("empty-state").hidden = tickets.length > 0 || state.loading;
  el("empty-title").textContent = state.history
    ? "Nenhum chamado resolvido"
    : "Nenhum chamado encontrado";
  el("empty-description").textContent =
    search || status
      ? "Tente mudar a busca ou o filtro."
      : state.user?.perfil === "Tecnico"
        ? "Os chamados disponíveis vão aparecer aqui."
        : "Use Novo chamado para pedir ajuda à equipe de TI.";
  el("list-count").textContent = state.loading
    ? "Carregando chamados…"
    : tickets.length + (tickets.length === 1 ? " chamado" : " chamados");
  el("count-open").textContent = state.tickets.filter(
    (t) => t.status === "Aberto",
  ).length;
  el("count-progress").textContent = state.tickets.filter(
    (t) => t.status === "EmAtendimento",
  ).length;
  el("count-resolved").textContent = state.tickets.filter(
    (t) => t.status === "Resolvido",
  ).length;
}

function renderDetail(focus = false) {
  const ticket = state.selected;
  el("detail-panel").hidden = !ticket;
  el("content-grid").classList.toggle("with-detail", Boolean(ticket));
  if (!ticket) return;
  el("detail-code").textContent = shortId(ticket.id);
  el("detail-title").textContent = ticket.titulo;
  el("detail-category").textContent = ticket.categoria;
  el("detail-description").textContent = ticket.descricao;
  el("detail-owner").textContent = !ticket.tecnicoId
    ? "Aguardando atribuição"
    : ticket.tecnicoId === state.user.id
      ? "Você"
      : "Técnico atribuído";
  badge(el("detail-status"), ticket.status);
  const technician = state.user.perfil === "Tecnico";
  el("assume-ticket").hidden = !technician || ticket.status !== "Aberto";
  el("attendance-form").hidden =
    !technician ||
    ticket.status !== "EmAtendimento" ||
    ticket.tecnicoId !== state.user.id;
  el("assignment-note").hidden =
    !technician ||
    ticket.status !== "EmAtendimento" ||
    ticket.tecnicoId === state.user.id;
  el("assignment-note").textContent =
    "Este chamado está em atendimento por outro técnico.";
  const events = ticket.historico.map((event) => {
    const item = node("li");
    const author =
      event.autorId === state.user.id
        ? state.user.nome
        : event.autorId === ticket.solicitanteId
          ? "Funcionário solicitante"
          : "Técnico responsável";
    const date = new Date(event.data).toLocaleString("pt-BR");
    item.append(
      node("strong", eventNames[event.tipo] ?? event.tipo),
      node("small", date + " · " + author),
    );
    if (event.descricao) item.append(node("p", event.descricao));
    return item;
  });
  el("timeline").replaceChildren(...events);
  if (focus) el("detail-code").focus();
}

async function refreshTickets() {
  if (!state.user) return;
  const sequence = ++state.listRequest;
  const generation = state.generation;
  state.loading = true;
  el("refresh").disabled = true;
  el("ticket-rows").setAttribute("aria-busy", "true");
  renderTickets();
  try {
    const tickets = await request("/chamados");
    if (sequence !== state.listRequest) return;
    state.tickets = tickets;
    renderTickets();
  } finally {
    if (generation === state.generation && sequence === state.listRequest) {
      state.loading = false;
      el("refresh").disabled = false;
      el("ticket-rows").setAttribute("aria-busy", "false");
      renderTickets();
    }
  }
}

async function openTicket(id) {
  if (state.busy) return;
  const sequence = ++state.detailRequest;
  notify("Carregando detalhes…");
  try {
    const ticket = await request("/chamados/" + encodeURIComponent(id));
    if (sequence !== state.detailRequest) return;
    state.selected = ticket;
    el("attendance-form").reset();
    renderDetail(true);
    renderTickets();
    notify();
  } catch (error) {
    if (sequence === state.detailRequest && state.user)
      notify(error.message, true);
  }
}

function setBusy(busy) {
  state.busy = busy;
  if (busy) state.detailRequest++;
  for (const button of document.querySelectorAll(
    "#assume-ticket, #attendance-form button, #new-form button, #cancel-new-top, #new-ticket, #close-detail, #refresh, #logout",
  )) {
    button.disabled = busy;
  }
  renderTickets();
}

async function mutate(action) {
  if (state.busy) return;
  const generation = state.generation;
  setBusy(true);
  try {
    await action();
  } catch (error) {
    if (state.user) notify(error.message, true);
  } finally {
    if (generation === state.generation) setBusy(false);
  }
}

async function updateTicket(action) {
  const ticket = state.selected;
  if (!ticket) return;
  const body =
    action === "assumir" ? undefined : { texto: el("attendance-text").value };
  await mutate(async () => {
    try {
      state.selected = await request("/chamados/" + ticket.id + "/" + action, {
        method: "POST",
        body,
      });
    } catch (error) {
      // Recarrega o estado depois de uma disputa, mantendo o texto digitado.
      if (state.user) {
        try {
          state.selected = await request("/chamados/" + ticket.id);
          renderDetail();
          await refreshTickets();
        } catch {
          /* A mensagem da operação original orienta a próxima tentativa. */
        }
      }
      throw error;
    }
    el("attendance-form").reset();
    renderDetail();
    notify(
      action === "assumir"
        ? "Você assumiu o atendimento."
        : action === "resolver"
          ? "Chamado resolvido. A solução foi registrada no histórico."
          : "Atualização registrada no histórico.",
    );
    try {
      await refreshTickets();
    } catch {
      notify(
        "A alteração foi salva. Use Atualizar para recarregar a lista.",
        true,
      );
    }
  });
}

el("login-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const button = event.submitter;
  button.disabled = true;
  el("login-error").hidden = true;
  const generation = state.generation;
  try {
    const login = await request("/auth/login", {
      method: "POST",
      anonymous: true,
      body: { email: el("email").value.trim(), senha: el("password").value },
    });
    state.token = login.accessToken;
    state.user = await request("/auth/me");
    el("password").value = "";
    el("login-screen").hidden = true;
    el("workspace").hidden = false;
    state.history = false;
    el("search").value = "";
    el("status-filter").value = "";
    const technician = state.user.perfil === "Tecnico";
    el("account-name").textContent = state.user.nome;
    el("account-role").textContent = technician
      ? "Técnico de TI"
      : "Funcionário";
    el("avatar").textContent = state.user.nome
      .split(" ")
      .filter(Boolean)
      .slice(0, 2)
      .map((n) => n[0])
      .join("");
    el("new-ticket").hidden = technician;
    el("nav-active-label").textContent = technician
      ? "Fila de chamados"
      : "Meus chamados";
    changeView(false);
    renderDetail();
    try {
      await refreshTickets();
    } catch (error) {
      if (state.user) notify(error.message, true);
    }
  } catch (error) {
    if (generation === state.generation) {
      signOut(error.message);
      el("email").focus();
    }
  } finally {
    button.disabled = false;
  }
});

function changeView(history) {
  state.history = history;
  const technician = state.user?.perfil === "Tecnico";
  el("page-title").textContent = history
    ? "Histórico de resolvidos"
    : technician
      ? "Fila de chamados"
      : "Meus chamados";
  el("page-description").textContent = history
    ? "Consulte as soluções e os atendimentos já concluídos."
    : technician
      ? "Organize os atendimentos e ajude a equipe a seguir em frente."
      : "Peça ajuda à equipe de TI e acompanhe cada etapa do atendimento.";
  el("list-title").textContent = history
    ? "Chamados resolvidos"
    : technician
      ? "Todos os chamados da equipe"
      : "Acompanhe seus chamados";
  el("nav-active").classList.toggle("active", !history);
  el("nav-history").classList.toggle("active", history);
  el("nav-active").setAttribute("aria-current", history ? "false" : "page");
  el("nav-history").setAttribute("aria-current", history ? "page" : "false");
  el("status-filter").value = history ? "Resolvido" : "";
  el("status-filter").disabled = history;
  document.title = el("page-title").textContent + " · Alunos Tech";
  notify();
  renderTickets();
}

el("toggle-password").addEventListener("click", () => {
  const show = el("password").type === "password";
  el("password").type = show ? "text" : "password";
  el("toggle-password").textContent = show ? "Ocultar" : "Mostrar";
  el("toggle-password").setAttribute("aria-pressed", String(show));
});
el("logout").addEventListener("click", () => signOut());
el("nav-active").addEventListener("click", () => changeView(false));
el("nav-history").addEventListener("click", () => changeView(true));
el("search").addEventListener("input", renderTickets);
el("status-filter").addEventListener("change", renderTickets);
el("refresh").addEventListener("click", async () => {
  try {
    await refreshTickets();
    if (state.user) notify("Lista atualizada.");
  } catch (error) {
    if (state.user) notify(error.message, true);
  }
});
el("close-detail").addEventListener("click", () => {
  state.detailRequest++;
  state.selected = null;
  renderDetail();
  renderTickets();
  el("search").focus();
});
el("new-ticket").addEventListener("click", () => {
  el("new-error").hidden = true;
  el("new-dialog").showModal();
});
for (const id of ["cancel-new", "cancel-new-top"])
  el(id).addEventListener("click", () => el("new-dialog").close());
el("new-dialog").addEventListener("cancel", (event) => {
  if (state.busy) event.preventDefault();
});
el("new-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  if (state.busy) return;
  const generation = state.generation;
  setBusy(true);
  el("new-error").hidden = true;
  try {
    const ticket = await request("/chamados", {
      method: "POST",
      body: {
        titulo: el("ticket-title").value,
        categoria: el("ticket-category").value,
        descricao: el("ticket-description").value,
      },
    });
    state.selected = ticket;
    el("new-form").reset();
    el("new-dialog").close();
    changeView(false);
    renderDetail(true);
    notify("Chamado " + shortId(ticket.id) + " enviado para a equipe de TI.");
    try {
      await refreshTickets();
    } catch {
      notify(
        "O chamado foi criado. Use Atualizar para recarregar a lista.",
        true,
      );
    }
  } catch (error) {
    if (state.user) {
      el("new-error").textContent = error.message;
      el("new-error").hidden = false;
    }
  } finally {
    if (generation === state.generation) setBusy(false);
  }
});
el("assume-ticket").addEventListener("click", () => updateTicket("assumir"));
el("attendance-form").addEventListener("submit", (event) => {
  event.preventDefault();
  updateTicket("atualizacoes");
});
el("resolve-ticket").addEventListener("click", () => {
  if (el("attendance-form").reportValidity()) updateTicket("resolver");
});
