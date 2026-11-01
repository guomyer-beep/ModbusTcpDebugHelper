// ModbusTCP 设备调试助手 - 前端脚本
const $ = (sel) => document.querySelector(sel);

async function apiPost(url, body) {
    const resp = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body ?? {})
    });
    return resp.json();
}
async function apiGet(url) { return (await fetch(url)).json(); }
async function apiDelete(url) { return (await fetch(url, { method: 'DELETE' })).json(); }

function showMsg(msg, ok) {
    const el = $('#msg');
    el.textContent = msg || '';
    el.className = 'msg ' + (ok ? 'ok' : 'err');
    if (!msg) el.classList.add('hidden');
}

function setStatus(connected, host, port) {
    const badge = $('#status-badge');
    badge.textContent = connected ? `已连接 ${host || ''}:${port || ''}` : '未连接';
    badge.className = 'badge ' + (connected ? 'ok' : 'off');
}

// 页面加载时预填默认目标（来自 appsettings.json）
async function loadDefault() {
    const d = await apiGet('/api/modbus/default');
    $('#host').value = d.host || '127.0.0.1';
    $('#port').value = d.port || 502;
}

async function refreshStatus() {
    const s = await apiGet('/api/modbus/status');
    setStatus(s.connected, s.host, s.port);
}

async function connect() {
    const host = $('#host').value.trim();
    const port = parseInt($('#port').value, 10);
    const r = await apiPost('/api/modbus/connect', { host, port });
    showMsg(r.message, r.success);
    await refreshStatus();
    refreshLogs();
}

async function disconnect() {
    const r = await apiPost('/api/modbus/disconnect');
    showMsg(r.message, r.success);
    await refreshStatus();
    refreshLogs();
}

async function read() {
    const startAddress = parseInt($('#read-addr').value, 10);
    const quantity = parseInt($('#read-qty').value, 10);
    const r = await apiPost('/api/modbus/read-holding', { startAddress, quantity });
    showMsg(r.message, r.success);
    if (r.success) {
        renderRegisters(r.registers);
        renderPacket(r.rawRequestHex, r.rawResponseHex);
    }
    refreshStatus();
    refreshLogs();
}

async function write() {
    const address = parseInt($('#write-addr').value, 10);
    const value = parseInt($('#write-value').value, 10);
    const r = await apiPost('/api/modbus/write-holding', { address, value });
    showMsg(r.message, r.success);
    if (r.success) renderPacket(r.rawRequestHex, r.rawResponseHex);
    refreshStatus();
    refreshLogs();
}

function renderRegisters(regs) {
    const tbody = $('#reg-table tbody');
    tbody.innerHTML = '';
    if (!regs || regs.length === 0) {
        tbody.innerHTML = '<tr><td colspan="3">无数据</td></tr>';
        return;
    }
    for (const reg of regs) {
        const tr = document.createElement('tr');
        const hex = '0x' + reg.value.toString(16).toUpperCase().padStart(4, '0');
        tr.innerHTML = `<td>${reg.address}</td><td>${reg.value}</td><td>${hex}</td>`;
        tbody.appendChild(tr);
    }
}

function renderPacket(tx, rx) {
    $('#tx-hex').textContent = tx || '(无)';
    $('#rx-hex').textContent = rx || '(无)';
}

// ---- 日志 ----
let currentTab = 'packets';

async function refreshLogs() {
    const list = await apiGet('/api/logs/' + currentTab + '?count=50');
    renderLogs(list);
}

function renderLogs(list) {
    const box = $('#log-list');
    box.innerHTML = '';
    if (!list || list.length === 0) {
        box.innerHTML = '<div class="log-empty">暂无日志</div>';
        return;
    }
    for (const item of list) {
        const div = document.createElement('div');
        div.className = 'log-item';
        if (currentTab === 'packets') {
            div.innerHTML =
                `<span class="dir ${item.direction.toLowerCase()}">${item.direction}</span>` +
                `<span class="time">${item.ts}</span>` +
                `<span class="label">${item.label}</span>` +
                `<code>${item.hex}</code>`;
        } else if (currentTab === 'registers') {
            div.innerHTML =
                `<span class="dir ${item.direction}">${item.direction === 'read' ? '读' : '写'}</span>` +
                `<span class="time">${item.ts}</span>` +
                `<span class="label">地址 ${item.address}，数量 ${item.quantity}</span>` +
                `<code>值 ${item.value ?? '-'}</code>`;
        } else {
            div.innerHTML =
                `<span class="time">${item.ts}</span>` +
                `<span class="label">${item.host}:${item.port}</span>` +
                `<code>${item.result}</code>`;
        }
        box.appendChild(div);
    }
}

function switchTab(tab) {
    currentTab = tab;
    document.querySelectorAll('.tab').forEach(t => t.classList.toggle('active', t.dataset.tab === tab));
    refreshLogs();
}

async function clearPackets() {
    await apiDelete('/api/logs/packets');
    refreshLogs();
}

// ---- 启动 ----
document.addEventListener('DOMContentLoaded', () => {
    $('#btn-connect').addEventListener('click', connect);
    $('#btn-disconnect').addEventListener('click', disconnect);
    $('#btn-read').addEventListener('click', read);
    $('#btn-write').addEventListener('click', write);
    $('#btn-refresh-log').addEventListener('click', refreshLogs);
    $('#btn-clear-log').addEventListener('click', clearPackets);
    document.querySelectorAll('.tab').forEach(t => t.addEventListener('click', () => switchTab(t.dataset.tab)));

    loadDefault();
    refreshStatus();
    refreshLogs();
    setInterval(refreshStatus, 3000); // 周期刷新连接状态
});