window.exuarchGitHub = (() => {
    const storageKey = 'exuarch.github';
    const api = 'https://api.github.com';

    const repoPattern = /^[\w.-]+\/[\w.-]+$/;
    const connection = () => {
        try {
            const saved = JSON.parse(localStorage.getItem(storageKey) || 'null');
            return saved && typeof saved.repo === 'string' && repoPattern.test(saved.repo) && typeof saved.token === 'string' ? saved : null;
        } catch {
            return null;
        }
    };
    const repoPath = repo => repo.split('/').map(encodeURIComponent).join('/');
    const headers = token => ({ Accept: 'application/vnd.github+json', Authorization: `Bearer ${token}`, 'X-GitHub-Api-Version': '2022-11-28' });
    const encode = text => {
        let binary = '';
        for (const byte of new TextEncoder().encode(text)) binary += String.fromCharCode(byte);
        return btoa(binary);
    };
    const decode = base64 => {
        const binary = atob((base64 || '').replace(/\s/g, ''));
        return new TextDecoder().decode(Uint8Array.from(binary, c => c.charCodeAt(0)));
    };
    const contentsUrl = (repo, path) => `${api}/repos/${repoPath(repo)}/contents/${path.split('/').map(encodeURIComponent).join('/')}`;
    const statusMessage = status => {
        if (status === 401) return 'GitHub rejected the token: it is wrong or has expired.';
        if (status === 403) return 'The token is not allowed to write to this repository: give it Contents, read and write.';
        if (status === 404) return 'There is no such repository, or the token is not allowed to see it.';
        return `GitHub returned status ${status}.`;
    };
    const unreachable = 'GitHub could not be reached. Check the connection and save again.';

    return {
        repo: () => connection()?.repo ?? null,

        repoUrl: folder => {
            const saved = connection();
            return saved ? `https://github.com/${repoPath(saved.repo)}/tree/HEAD/${encodeURIComponent(folder)}` : 'https://github.com/';
        },

        fileName: name => (name || '').replace(/[\\/:*?"<>|\u0000-\u001f]/g, '-').trim() || 'unnamed',

        connect: async (repo, token) => {
            repo = (repo || '').trim().replace(/^https:\/\/github\.com\//, '').replace(/\.git$/, '').replace(/\/$/, '');
            token = (token || '').trim();
            if (!repoPattern.test(repo)) return 'Write the repository as owner/name, for example octocat/my-exuarch.';
            if (!token) return 'Paste the token.';
            try {
                const response = await fetch(`${api}/repos/${repoPath(repo)}`, { headers: headers(token), cache: 'no-store' });
                if (!response.ok) return statusMessage(response.status);
                localStorage.setItem(storageKey, JSON.stringify({ repo, token }));
                return null;
            } catch {
                return unreachable;
            }
        },

        disconnect: () => {
            try { localStorage.removeItem(storageKey); } catch { }
        },

        read: async path => {
            const saved = connection();
            if (!saved) return { missing: true };
            try {
                const response = await fetch(contentsUrl(saved.repo, path), { headers: headers(saved.token), cache: 'no-store' });
                if (response.status === 404) return { missing: true };
                if (!response.ok) return { error: statusMessage(response.status) };
                const file = await response.json();
                if (file.encoding !== 'base64') return { error: `${path} is too large to read through the GitHub API.` };
                return { text: decode(file.content) };
            } catch {
                return { error: unreachable };
            }
        },

        write: async (path, content, message) => {
            const saved = connection();
            if (!saved) return null;
            const url = contentsUrl(saved.repo, path);
            try {
                for (let attempt = 0; attempt < 2; attempt++) {
                    const current = await fetch(url, { headers: headers(saved.token), cache: 'no-store' });
                    let sha;
                    if (current.ok) {
                        const file = await current.json();
                        if (file.encoding === 'base64' && decode(file.content) === content) return null;
                        sha = file.sha;
                    } else if (current.status !== 404) {
                        return statusMessage(current.status);
                    }
                    const put = await fetch(url, { method: 'PUT', headers: headers(saved.token), body: JSON.stringify({ message, content: encode(content), sha }) });
                    if (put.ok) return null;
                    if (put.status !== 409 && put.status !== 422) return statusMessage(put.status);
                }
                return 'The file changed on GitHub while saving. Save again.';
            } catch {
                return unreachable;
            }
        },
    };
})();
