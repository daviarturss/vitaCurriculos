window.vita = {
    postForm: async function (url, payload) {
        try {
            const body = new URLSearchParams();
            Object.keys(payload || {}).forEach(k => body.append(k, payload[k] ?? ""));
            const response = await fetch(url, {
                method: "POST",
                headers: { "Content-Type": "application/x-www-form-urlencoded" },
                body: body.toString(),
                credentials: "same-origin"
            });
            let data = {};
            try { data = await response.json(); } catch { }
            return Object.assign({ ok: response.ok, status: response.status, error: data.error || null }, data);
        } catch (e) {
            return { ok: false, error: "Falha de conexão com o servidor local." };
        }
    },

    logout: async function () {
        try { await fetch("/auth/logout", { method: "POST", credentials: "same-origin" }); } catch { }
        window.location.href = "/login";
    },

    copy: async function (text) {
        try {
            await navigator.clipboard.writeText(text || "");
            return true;
        } catch {
            const area = document.createElement("textarea");
            area.value = text || "";
            document.body.appendChild(area);
            area.select();
            try { document.execCommand("copy"); } catch { }
            document.body.removeChild(area);
            return true;
        }
    },

    printPage: function () { window.print(); },

    fitPreview: function (frame) {
        if (!frame) return;
        const paper = frame.querySelector(".paper");
        if (!paper) return;
        const apply = function () {
            const available = frame.clientWidth;
            if (!available) return;
            const scale = Math.min(1, available / 794);
            paper.style.transform = "scale(" + scale + ")";
            paper.style.transformOrigin = "top left";
            frame.style.height = (1123 * scale) + "px";
        };
        apply();
        if (window.ResizeObserver) {
            if (frame._vitaObserver) frame._vitaObserver.disconnect();
            frame._vitaObserver = new ResizeObserver(apply);
            frame._vitaObserver.observe(frame);
        } else {
            window.addEventListener("resize", apply);
        }
    },

    scrollToEnd: function (el) {
        if (!el) return;
        el.scrollTop = el.scrollHeight;
    },

    focus: function (el) { if (el) el.focus(); },

    download: function (url) {
        const link = document.createElement("a");
        link.href = url;
        link.rel = "noopener";
        document.body.appendChild(link);
        link.click();
        link.remove();
    }
};
