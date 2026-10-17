"use strict";

const sessionBox = document.querySelector("#session");
const profileBox = document.querySelector("#profile");
const resultBox = document.querySelector("#result");
const createFields = document.querySelector("#createFields");
const logoutButton = document.querySelector("#logout");

const registerForm = document.querySelector("#register");
const loginForm = document.querySelector("#login");
const createForm = document.querySelector("#create");

let busy = false;

class UiError extends Error {}

async function apiFetch(path, options = {}) {
  const url = new URL(path, location.origin);

  if (url.origin !== location.origin) {
    throw new UiError("Потрібна адреса цього застосунку.");
  }

  return fetch(url, {
    ...options,
    credentials: "same-origin",
    cache: "no-store",
  });
}

function postJson(path, body) {
  return apiFetch(path, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
  });
}

async function readJson(response) {
  const contentType = response.headers.get("content-type") ?? "";

  if (!contentType.toLowerCase().includes("application/json")) {
    throw new UiError("Сервер повернув неочікуваний формат відповіді.");
  }

  return response.json();
}

function showSession(state, profile = null) {
  profileBox.textContent = "";

  createFields.disabled = state !== "authenticated";
  logoutButton.disabled = state !== "authenticated";

  if (state === "authenticated") {
    sessionBox.textContent = "Сеанс активний";

    // Показуємо лише дозволені поля DTO.
    profileBox.textContent = `ID: ${profile.id}\nКористувач: ${profile.userName}`;
  } else if (state === "anonymous") {
    sessionBox.textContent = "Не ввійшли";
  } else {
    sessionBox.textContent = "Стан сеансу не визначено";
  }
}

async function refreshProfile() {
  // Старі дані не залишаються на екрані під час помилки.
  showSession("unknown");

  const response = await apiFetch("/api/me");

  if (response.status === 401) {
    showSession("anonymous");
    return 401;
  }

  if (!response.ok) {
    throw new UiError(`Не вдалося отримати профіль: HTTP ${response.status}.`);
  }

  const profile = await readJson(response);

  if (typeof profile.id !== "string" || typeof profile.userName !== "string") {
    throw new UiError("Некоректний формат профілю.");
  }

  showSession("authenticated", profile);
  return response.status;
}

async function run(action) {
  // Запити сторінки виконуються послідовно.
  if (busy) return;

  busy = true;

  try {
    await action();
  } catch (error) {
    showSession("unknown");

    resultBox.textContent =
      error instanceof UiError
        ? error.message
        : "Помилка мережі або некоректна відповідь API. Повторіть дію.";
  } finally {
    busy = false;
  }
}

registerForm.addEventListener("submit", (event) => {
  event.preventDefault();

  run(async () => {
    const fields = new FormData(registerForm);

    const body = {
      userName: String(fields.get("userName") ?? ""),
      email: String(fields.get("email") ?? ""),
      displayName: String(fields.get("displayName") ?? ""),
      password: String(fields.get("password") ?? ""),
    };

    registerForm.elements.namedItem("password").value = "";
    fields.delete("password");

    let response;

    try {
      response = await postJson("/api/auth/register", body);
    } finally {
      body.password = "";
    }

    if (response.status !== 201) {
      resultBox.textContent =
        `Реєстрація: HTTP ${response.status}. ` +
        "Перевірте поля, вимоги до пароля або зайнятість імені/email.";
      return;
    }

    const registered = await readJson(response);

    if (typeof registered.userName !== "string") {
      throw new UiError("Некоректна відповідь реєстрації.");
    }

    loginForm.elements.namedItem("userName").value = registered.userName;

    // Реєстрація сама по собі не виконує login.
    await refreshProfile();

    resultBox.textContent =
      "Реєстрація: HTTP 201. Обліковий запис створено. " +
      "Тепер увійдіть через форму входу.";
  });
});

loginForm.addEventListener("submit", (event) => {
  event.preventDefault();

  run(async () => {
    const fields = new FormData(loginForm);

    const body = {
      userName: String(fields.get("userName") ?? ""),
      password: String(fields.get("password") ?? ""),
    };

    loginForm.elements.namedItem("password").value = "";
    fields.delete("password");

    let response;

    try {
      response = await postJson("/api/auth/login", body);
    } finally {
      body.password = "";
    }

    await refreshProfile();

    if (response.status === 204) {
      resultBox.textContent = "Вхід: HTTP 204. Профіль оновлено з сервера.";
    } else if (response.status === 401) {
      resultBox.textContent =
        "Вхід: HTTP 401. Неправильне ім'я користувача або пароль.";
    } else if (response.status === 429) {
      resultBox.textContent =
        "Вхід: HTTP 429. Забагато спроб. " +
        "Повторіть після завершення хвилинного вікна.";
    } else {
      throw new UiError(`Помилка входу: HTTP ${response.status}.`);
    }
  });
});

createForm.addEventListener("submit", (event) => {
  event.preventDefault();

  run(async () => {
    const fields = new FormData(createForm);

    const occurredAt = new Date(String(fields.get("occurredAtUtc") ?? ""));

    if (Number.isNaN(occurredAt.getTime())) {
      resultBox.textContent = "Укажіть коректну дату й час події.";
      return;
    }

    const body = {
      title: String(fields.get("title") ?? ""),
      description: String(fields.get("description") ?? ""),
      severity: String(fields.get("severity") ?? ""),
      occurredAtUtc: occurredAt.toISOString(),
    };

    const response = await postJson("/api/incidents", body);

    if (response.status === 401) {
      showSession("anonymous");
      resultBox.textContent = "Створення: HTTP 401. Потрібно увійти повторно.";
      return;
    }

    if (response.status === 400) {
      resultBox.textContent =
        "Створення: HTTP 400. Перевірте довжину полів, опис і дату.";
      return;
    }

    if (response.status === 409) {
      resultBox.textContent =
        "Створення: HTTP 409. Незакритий інцидент із таким заголовком існує.";
      return;
    }

    if (response.status !== 201) {
      throw new UiError(`Помилка створення: HTTP ${response.status}.`);
    }

    const created = await readJson(response);

    if (typeof created.id !== "string") {
      throw new UiError("Некоректна відповідь створення інциденту.");
    }

    resultBox.textContent = `Інцидент створено: HTTP 201.\nID: ${created.id}`;

    createForm.reset();
  });
});

document.querySelector("#me").addEventListener("click", () => {
  run(async () => {
    const status = await refreshProfile();

    resultBox.textContent = `Профіль перевірено на сервері: HTTP ${status}.`;
  });
});

logoutButton.addEventListener("click", () => {
  run(async () => {
    const response = await apiFetch("/api/auth/logout", {
      method: "POST",
    });

    if (response.status !== 204) {
      throw new UiError(`Помилка виходу: HTTP ${response.status}.`);
    }

    showSession("unknown");
    resultBox.textContent = "";
    createForm.reset();

    const status = await refreshProfile();

    if (status !== 401) {
      throw new UiError(
        "Вихід не підтверджено: профіль залишається доступним.",
      );
    }

    resultBox.textContent = "Вихід: HTTP 204. Повторний GET /api/me: HTTP 401.";
  });
});

run(refreshProfile);
