// Behavioral replay of the real hook with a deterministic clock, Gameface binding, and img loader.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const ts = require('typescript');
const source = fs.readFileSync(path.join(__dirname, '../src/mods/optional-ail-icons.ts'), 'utf8');
let clock = 10000, timers = [], nextTimer = 1, requests = [], images = [], handlers = new Map(), current;
const setTimer = (fn, ms) => { const id = nextTimer++; timers.push({ id, at: clock + ms, fn }); return id; };
const clearTimer = id => { timers = timers.filter(t => t.id !== id); };
function tick(ms) {
  const end = clock + ms;
  for (;;) {
    timers.sort((a,b) => a.at - b.at);
    if (!timers.length || timers[0].at > end) break;
    const t = timers.shift(); clock = t.at; t.fn();
  }
  clock = end;
}
const engine = { on(k,fn) { handlers.set(k,fn); }, off(k,fn) { if(handlers.get(k) === fn) handlers.delete(k); }, trigger(k,payload) {
  if(k !== 'ssp.requestOptionalIcon') return;
  requests.push({ at: clock, payload });
  const [i,s] = String(payload).split(':').map(Number);
  setTimer(() => handlers.get('ssp.optionalIconResult.update')?.(JSON.stringify({ i,s,url:`coui://ail/BuildingPrefab.${current.name(i)}.png` })),1);
}};
const React = { useState(initial) { const i=current.cursor++; if(!(i in current.states)) current.states[i]=initial; return [current.states[i], fn=>{current.states[i]=typeof fn==='function'?fn(current.states[i]):fn;current.dirty=true;}]; }, useRef(initial) { const i=current.cursor++; if(!(i in current.states)) current.states[i]={current:initial}; return current.states[i]; }, useEffect(fn,deps) { const i=current.cursor++; const prior=current.effects[i]; if(!prior || deps.some((x,j)=>x!==prior.deps[j])) {prior?.cleanup?.(); current.effects[i]={deps, cleanup:fn()};} } };
const sandbox = {module:{exports:{}}, exports:{}, require(k){ if(k==='react')return {__esModule:true,default:React}; throw Error(k); }, window:{engine}, document:{createElement(){const image={onload:null,onerror:null,set src(value){ this._src=value; images.push({at:clock,url:value});setTimer(()=>this.onload?.(),1); }};return image;}}, setTimeout:setTimer, clearTimeout:clearTimer, Date:{now:()=>clock}, console};
sandbox.exports=sandbox.module.exports;
vm.runInNewContext(ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2020,esModuleInterop:true}}).outputText,sandbox,{filename:'optional-ail-icons.js'});
const hook=sandbox.module.exports.useOptionalAilIcons;
function mount(names) { const app={states:[],effects:[],cursor:0,dirty:false,names, name(i){return this.names[i]}}; current=app;return app; }
function render(app,visible,buffer=[]) {current=app;app.cursor=0;return hook(visible.map(i=>({i,key:app.name(i)})),buffer.map(i=>({i,key:app.name(i)})));}
function unmount(app){ for(const e of app.effects) e?.cleanup?.(); }
const a=mount(['EU_L1','NA_L1']);render(a,[0,1]);tick(500);
assert.equal(images.length,2,'both visible AIL pictures must load');
assert.ok(images[1].at-images[0].at <= 100,'visible images should load at roughly half the old interval');
const firstCount=images.length;unmount(a);
const b=mount(['EU_L1','NA_L1']);let cached=render(b,[0]);tick(500);
assert.equal(cached.get(0),'coui://ail/BuildingPrefab.EU_L1.png','reopened panel shows cached original image');
assert.equal(images.length,firstCount,'reopen must not load the same AIL picture again');unmount(b);
const c=mount(['NA_L1','EU_L1']);cached=render(c,[1]);tick(500);
assert.equal(cached.get(1),'coui://ail/BuildingPrefab.EU_L1.png','same source prefab survives save reorder');
assert.equal(images.length,firstCount,'switching saves should reuse an identical original building image');
render(c,[0]);tick(500);
assert.equal(images.length,firstCount,'the other original building should also reuse its image');
c.names[0]='FILTER_NEW';render(c,[0]);tick(500);
assert.equal(images.length,firstCount+1,'different building at a reused index must not inherit another image');
tick(91000); // panel c has remained mounted
c.names[0]='FILTER_AFTER_WAIT';render(c,[0]);tick(500);
assert.equal(images.length,firstCount+2,'newly visible filtered item must load even after panel has been open >90s');
assert.ok(images[firstCount+1].at - requests.at(-1)?.at < 1000,'new image should start promptly');
unmount(c);
console.log('optional AIL cache / save reorder / long-open filter replay passed');
