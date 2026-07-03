const AXES = ["X","Y","Z","RX","RY","RZ","S0","S1"];
let ws = null;

const SLIDERS = [
  {k:"center",  name:"Autocentrado",              min:0, max:2, step:0.01},
  {k:"lateral", name:"Tirón lateral",             min:0, max:1, step:0.01},
  {k:"impact",  name:"Golpes / impactos",         min:0, max:2, step:0.01},
  {k:"rumble",  name:"Vibración (curbs/bloqueo)", min:0, max:1, step:0.01},
  {k:"drift",   name:"Aligerar al derrapar",      min:0, max:1, step:0.01},
];
const slidersHost = document.getElementById("sliders");
const sliderEls = {};
function fill(inp){
  const pct = ((inp.value - inp.min) / (inp.max - inp.min)) * 100;
  inp.style.background = `linear-gradient(90deg,var(--acc) ${pct}%,#2c2f33 ${pct}%)`;
}
SLIDERS.forEach(s => {
  const row = document.createElement("div"); row.className = "srow";
  row.innerHTML = `<div class="slbl"><span>${s.name}</span><span class="sv" id="sv_${s.k}">0.00</span></div>
    <input type="range" id="sl_${s.k}" min="${s.min}" max="${s.max}" step="${s.step}">`;
  slidersHost.appendChild(row);
  const inp = row.querySelector("input"), val = row.querySelector(".sv");
  sliderEls[s.k] = {inp, val};
  inp.addEventListener("input", () => { val.textContent = parseFloat(inp.value).toFixed(2); fill(inp); send({t:"ffb", k:s.k, v:parseFloat(inp.value)}); });
});

const MAPS = [{tgt:"steer",name:"Dirección"},{tgt:"throttle",name:"Gas"},{tgt:"brake",name:"Freno"},{tgt:"clutch",name:"Embrague"}];
const mapHost = document.getElementById("mapRows"), mapEls = {};
MAPS.forEach(m => {
  const row = document.createElement("div"); row.className = "mrow";
  const opts = AXES.map(a => `<option value="${a}">${a}</option>`).join("");
  row.innerHTML = `<div class="mlbl">${m.name}</div><select id="ax_${m.tgt}">${opts}</select>
    <label class="mInv"><span class="switch"><input type="checkbox" id="inv_${m.tgt}"><span class="slider"></span></span>inv</label>`;
  mapHost.appendChild(row);
  const sel = row.querySelector("select"), inv = row.querySelector("input");
  mapEls[m.tgt] = {sel, inv};
  const fire = () => send({t:"map", tgt:m.tgt, ax:sel.value, inv:inv.checked});
  sel.addEventListener("change", fire); inv.addEventListener("change", fire);
});

const btnGrid = document.getElementById("btnGrid"), dots = [];
for (let i=0;i<24;i++){ const d=document.createElement("div"); d.className="bdot"; d.textContent=i; btnGrid.appendChild(d); dots.push(d); }
function dp(id,on){ const e=document.getElementById("dp_"+id); if(e) e.classList.toggle("on",on); }

const invffb = document.getElementById("invffb");
invffb.addEventListener("change", ()=> send({t:"invffb", v:invffb.checked}));
const pup = document.getElementById("pup"), pdn = document.getElementById("pdn");
const firePaddle = ()=> send({t:"paddle", up:parseInt(pup.value)||0, down:parseInt(pdn.value)||0});
pup.addEventListener("change", firePaddle); pdn.addEventListener("change", firePaddle);
document.getElementById("quit").addEventListener("click", ()=>{ send({t:"quit"}); });

function connect(){
  ws = new WebSocket("ws://127.0.0.1:8770/ws");
  ws.onmessage = (ev)=>{ let d; try{d=JSON.parse(ev.data);}catch(e){return;} if(d.t==="init")onInit(d); else if(d.t==="live")onLive(d); };
  ws.onclose = ()=>{ setStatus(false,false); setTimeout(connect,1500); };
  ws.onerror = ()=>{ try{ws.close();}catch(e){} };
}
function send(o){ if(ws && ws.readyState===1) ws.send(JSON.stringify(o)); }
connect();

function onInit(d){
  setS("center",d.center); setS("lateral",d.lateral); setS("impact",d.impact); setS("rumble",d.rumble); setS("drift",d.drift);
  invffb.checked=!!d.invffb;
  setMap("steer",d.steerAx,d.steerInv); setMap("throttle",d.thrAx,d.thrInv); setMap("brake",d.brkAx,d.brkInv); setMap("clutch",d.cluAx,d.cluInv);
  pup.value=d.pup; pdn.value=d.pdn;
}
function setS(k,v){ const e=sliderEls[k]; if(!e||v==null)return; e.inp.value=v; e.val.textContent=parseFloat(v).toFixed(2); fill(e.inp); }
function setMap(tgt,ax,inv){ const e=mapEls[tgt]; if(!e)return; e.sel.value=(AXES.indexOf(ax)>=0?ax:"X"); e.inv.checked=!!inv; }

const wheelSvg = document.getElementById("wheelSvg");
function onLive(d){
  const s = Math.max(-1, Math.min(1, d.steer||0));
  wheelSvg.style.transform = "rotate(" + (s*130).toFixed(1) + "deg)";
  document.getElementById("steerVal").textContent = Math.round(s*130) + "°";
  ped("thr",d.thr); ped("brk",d.brk); ped("clu",d.clu);
  const b=d.btn||[]; for(let i=0;i<24;i++) dots[i].classList.toggle("on",!!b[i]);
  const p=(d.pov==null?-1:d.pov);
  dp("up", p===31500||p===0||p===4500);
  dp("rt", p===4500||p===9000||p===13500);
  dp("dn", p===13500||p===18000||p===22500);
  dp("lf", p===22500||p===27000||p===31500);
  setStatus(d.wheel,d.five,d.name);
}
function ped(id,v){ v=Math.max(0,Math.min(1,v||0)); document.getElementById(id+"Fill").style.height=(v*100)+"%"; document.getElementById(id+"Val").textContent=(v*100).toFixed(0)+"%"; }

function setStatus(wheel,five,name){
  const pw=document.getElementById("pillWheel"), pf=document.getElementById("pillFive");
  document.getElementById("wheelTxt").textContent = wheel ? "Volante OK" : "Sin volante";
  pw.className = "pill " + (wheel?"on":"warn");
  document.getElementById("fiveTxt").textContent = five ? "FiveM ✓" : "FiveM";
  pf.className = "pill " + (five?"on":"");
}
