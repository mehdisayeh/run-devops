(() => {
    'use strict';
    const products = JSON.parse(document.getElementById('product-data').textContent);
    const byId = new Map(products.map(p => [p.Id, p]));
    const money = value => new Intl.NumberFormat('en-IE', { style: 'currency', currency: 'EUR' }).format(value);
    const key = 'forme.cart.v1';
    let cart = [], storageAvailable = true, toastTimer;
    const normalize = items => Array.isArray(items) ? items.reduce((result, item) => {
        const p = byId.get(item?.id);
        if (p && p.StockQuantity > 0 && Number.isInteger(item.quantity) && item.quantity > 0) {
            const existing = result.find(i => i.id === p.Id);
            if (existing) existing.quantity = Math.min(p.StockQuantity, existing.quantity + item.quantity);
            else result.push({ id: p.Id, quantity: Math.min(p.StockQuantity, item.quantity) });
        }
        return result;
    }, []) : [];
    try { cart = normalize(JSON.parse(localStorage.getItem(key) || '[]')); } catch { cart = []; }
    function notify(message) {
        const toast = document.getElementById('cart-message');
        toast.textContent = message; toast.classList.add('visible');
        clearTimeout(toastTimer); toastTimer = setTimeout(() => toast.classList.remove('visible'), 3500);
    }
    function updateCount() { document.getElementById('cart-count').textContent = cart.reduce((sum, item) => sum + item.quantity, 0); }
    function save() {
        try { localStorage.setItem(key, JSON.stringify(cart)); }
        catch { storageAvailable = false; notify('Browser storage is unavailable. Your bag will last for this page only.'); }
        updateCount();
        const notice = document.getElementById('storage-notice');
        if (notice && !storageAvailable) notice.textContent = 'Browser storage is unavailable. Your bag cannot be saved.';
    }
    document.querySelectorAll('[data-add-cart]').forEach(button => button.addEventListener('click', () => {
        const product = byId.get(Number(button.dataset.addCart));
        const input = button.dataset.quantityInput ? document.getElementById(button.dataset.quantityInput) : null;
        if (input && !input.reportValidity()) return;
        const quantity = input ? Number(input.value) : 1;
        if (!product || !Number.isInteger(quantity) || quantity < 1 || product.StockQuantity < 1) return;
        const item = cart.find(i => i.id === product.Id);
        if ((item?.quantity || 0) + quantity > product.StockQuantity) { notify(`Only ${product.StockQuantity} available. Check your bag quantity.`); return; }
        if (item) item.quantity += quantity; else cart.push({ id: product.Id, quantity });
        save();
        if (storageAvailable) notify(`${product.Name} added to your bag.`);
    }));
    // Build saved bag content with DOM APIs, never interpolate stored values into HTML.
    function element(tag, className, text) { const el = document.createElement(tag); if(className) el.className = className; if(text !== undefined) el.textContent = text; return el; }
    function renderCart() {
        const container = document.getElementById('cart-items'); if (!container) return;
        container.replaceChildren();
        for (const item of cart) {
            const p = byId.get(item.id), row = element('article', 'cart-item');
            const imageLink = element('a'); imageLink.href = `/Home/Details/${p.Id}`;
            const image = element('img'); image.src = `/images/${p.ImageFile}`; image.alt = p.Name; image.width = 110; image.height = 110; imageLink.append(image);
            const body = element('div'), title = element('h2'), link = element('a', '', p.Name); link.href = imageLink.href; title.append(link);
            const label = element('label', '', 'Quantity'); label.htmlFor = `quantity-${p.Id}`;
            const input = element('input'); input.id = label.htmlFor; input.type = 'number'; input.min = '1'; input.max = String(p.StockQuantity); input.value = String(item.quantity); input.setAttribute('aria-label', `Quantity for ${p.Name}`);
            input.addEventListener('change', () => {
                if (!input.reportValidity()) { input.value = String(item.quantity); return; }
                item.quantity = Number(input.value); save(); renderCart();
                document.getElementById(input.id)?.focus();
            }); label.append(input); body.append(title, element('p', '', `${p.Brand} · ${money(p.Price)} each`), label);
            const end = element('div', 'cart-item-end'), remove = element('button', 'text-link', 'Remove'); remove.type = 'button'; remove.setAttribute('aria-label', `Remove ${p.Name}`);
            remove.addEventListener('click', () => { cart = cart.filter(i => i.id !== p.Id); save(); renderCart(); notify(`${p.Name} removed.`); document.querySelector('#cart-items button, #empty-cart a')?.focus(); });
            end.append(element('strong', '', money(p.Price * item.quantity)), remove); row.append(imageLink, body, end); container.append(row);
        }
        document.getElementById('empty-cart').hidden = cart.length > 0;
        document.getElementById('clear-cart').hidden = cart.length === 0;
        document.getElementById('cart-subtotal').textContent = money(cart.reduce((sum, i) => sum + byId.get(i.id).Price * i.quantity, 0));
    }
    document.getElementById('clear-cart')?.addEventListener('click', () => { cart = []; save(); renderCart(); notify('Your bag is now empty.'); });
    window.addEventListener('storage', e => { if (e.key === key || e.key === null) { try { cart = normalize(JSON.parse(e.newValue || '[]')); } catch { cart = []; } updateCount(); renderCart(); } });
    updateCount(); renderCart();
    const grid = document.getElementById('product-grid'), search = document.getElementById('nav-search');
    if (grid) {
        const cards = [...grid.querySelectorAll('[data-product-card]')], filters = [...document.querySelectorAll('[data-filter]')], sort = document.getElementById('product-sort');
        const params = new URLSearchParams(location.search); let category = params.get('category') || '';
        if (!filters.some(f => f.dataset.filter === category)) category = '';
        search.value = params.get('q') || '';
        function apply() {
            const query = search.value.trim().toLowerCase(); let count = 0;
            const sorted = [...cards].sort((a,b) => {
                switch(sort.value) {
                    case 'price-asc': return Number(a.dataset.price) - Number(b.dataset.price);
                    case 'price-desc': return Number(b.dataset.price) - Number(a.dataset.price);
                    case 'rating': return Number(b.dataset.rating) - Number(a.dataset.rating);
                    case 'name': return a.dataset.name.localeCompare(b.dataset.name);
                    default: return cards.indexOf(a) - cards.indexOf(b);
                }
            });
            sorted.forEach(card => { card.hidden = !!((category && card.dataset.category !== category) || !card.dataset.search.toLowerCase().includes(query)); if(!card.hidden) count++; grid.append(card); });
            filters.forEach(button => { const active = button.dataset.filter === category; button.classList.toggle('active', active); button.setAttribute('aria-pressed', String(active)); });
            document.getElementById('results-count').textContent = `${count} ${count === 1 ? 'product' : 'products'}${category ? ` in ${category}` : ' in the collection'}${query ? ` matching “${search.value.trim()}”` : ''}`;
            document.getElementById('no-results').hidden = count !== 0;
            const url = new URL(location.href); if(query) url.searchParams.set('q', search.value.trim()); else url.searchParams.delete('q'); if(category) url.searchParams.set('category', category); else url.searchParams.delete('category');
            history.replaceState(null, '', url);
        }
        filters.forEach(button => button.addEventListener('click', () => { category = button.dataset.filter; apply(); }));
        search.addEventListener('input', apply); sort.addEventListener('change', apply);
        search.form.addEventListener('submit', e => { e.preventDefault(); apply(); document.getElementById('collection').scrollIntoView(); });
        document.getElementById('clear-filters').addEventListener('click', () => { category = ''; search.value = ''; sort.value = 'featured'; apply(); });
        apply();
        if (params.has('q') || params.has('category')) document.getElementById('collection').scrollIntoView();
    }
    document.addEventListener('keydown', e => { if(e.key === '/' && !['INPUT','TEXTAREA','SELECT'].includes(document.activeElement.tagName)) { e.preventDefault(); search.focus(); } });
})();
