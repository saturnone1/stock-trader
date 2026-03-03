// Auth JS interop helpers — browser-side fetch for cookie-based auth
export async function authFetch(url, method, body) {
    try {
        const opts = {
            method: method,
            credentials: 'same-origin',
            headers: {}
        };
        if (body) {
            opts.headers['Content-Type'] = 'application/json';
            opts.body = JSON.stringify(body);
        }
        const resp = await fetch(url, opts);
        let data = null;
        const ct = resp.headers.get('content-type') || '';
        if (ct.includes('application/json')) {
            try { data = await resp.json(); } catch { }
        } else {
            try { data = await resp.text(); } catch { }
        }
        return { ok: resp.ok, status: resp.status, data: data };
    } catch (e) {
        return { ok: false, status: 0, data: e.message };
    }
}
