// Fixture isolado no navegador. Requer playwright-core e Chromium.
const {chromium} = require('playwright-core');
const assert = require('node:assert/strict');
const main = process.env.ULTIMA_MODE === 'main';
const fixture = {kind:'place', uid:'test-backpack-stump', asset:'prop.stump', x:-0.85, z:2, rot:0, scale:1};
const site = process.env.ULTIMA_SITE || 'https://sufficit.github.io/sufficit-game-ultima/';
const touch = process.env.ULTIMA_TOUCH === '1';
const deviceScaleFactor = Number(process.env.ULTIMA_DPR || 1);
const url = site+'?live=0&rede=0&api=http%3A%2F%2F127.0.0.1%3A19999'+(main?'&render=main&gpu=0':'');
const scene = page => main ? page : page.workers()[0];
async function project(target, point) {
    return target.evaluate(({name,x,z})=>{
        const s = BABYLON.EngineStore.LastCreatedScene;
        const engine = s.getEngine();
        const world = name ? s.getMeshByName(name).getBoundingInfo().boundingBox.centerWorld :
            s.pickWithRay(new BABYLON.Ray(new BABYLON.Vector3(x,100,z),new BABYLON.Vector3(0,-1,0)),m=>m.name.startsWith('ground-')).pickedPoint;
        const viewport = s.activeCamera.viewport.toGlobal(engine.getRenderWidth(),engine.getRenderHeight());
        const screen = BABYLON.Vector3.Project(world,BABYLON.Matrix.Identity(),s.getTransformMatrix(),viewport);
        const scale = engine.getHardwareScalingLevel();
        return {x:screen.x*scale,y:screen.y*scale,alt:world.y};
    },point);
}
async function bag(page) {
    await page.getByRole('button',{name:'Abrir painel do jogo'}).click();
    await page.getByText('Mochila',{exact:true}).click();
}
async function ready(page) {
    // domcontentloaded: na bancada (render por software) o evento load demora além
    // dos 30 s padrão do Playwright; o ready() abaixo já espera o jogo de verdade.
    await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 90000 });
    await until(()=>page.evaluate(async()=>{
        if (!window.game?.glb?.status || !game?.build?.state) return false;
        const map=await game.build.state(), glb=await game.glb.status();
        return map?.chunks>=25 && glb?.loaded.some(id=>id.startsWith('prop.stump')) && (await game.models.status()).queue===0;
    }));
}
async function lookIntoExcavation(target) {
    // Mantém o fundo visível para atingir o mesmo ponto em cada camada.
    await target.evaluate(async()=>{
        const s=BABYLON.EngineStore.LastCreatedScene;
        s.activeCamera.beta=0.3;
        await new Promise(resolve=>s.onAfterRenderObservable.addOnce(()=>resolve()));
    });
}
async function preview(target) {
    return target.evaluate(()=>{
        const scene=BABYLON.EngineStore.LastCreatedScene;
        const active = scene.meshes.filter(mesh=>mesh.isEnabled() && mesh.name.startsWith('collect-'));
        const ring = active.find(mesh=>mesh.name==='collect-ring-1');
        const center = ring?.getBoundingInfo().boundingBox.centerWorld;
        return {
            objects:active.filter(mesh=>mesh.name.startsWith('collect-preview:')).map(mesh=>mesh.parent.name),
            ground:center ? {x:center.x,y:center.y,z:center.z} : null
        };
    });
}
async function records(page) {
    return page.evaluate(()=>new Promise((resolve,reject)=>{
        const db=indexedDB.open('ultima-backpack',1);
        db.onerror=()=>reject(db.error);
        db.onsuccess=()=>{
            const req=db.result.transaction('collected').objectStore('collected').getAll();
            req.onsuccess=()=>{db.result.close();resolve(req.result)};
            req.onerror=()=>reject(req.error);
        };
    }));
}
async function until(check, label='A alteração esperada não apareceu', timeoutMs=180000) {
    // Prontidão real medida na bancada (render por software + terreno WASM sem AOT):
    // chunks>=25 aos ~52 s e fila de modelos zerando aos ~119 s. A janela cobre isso
    // com folga sem estourar o teto de 600 s do executor.
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        if (await check()) return;
        await new Promise(resolve=>setTimeout(resolve,250));
    }
    throw Error(label);
}
(async()=>{
    const browser=await chromium.launch({executablePath:process.env.ULTIMA_CHROMIUM || undefined,
        args:(process.env.ULTIMA_CHROMIUM_ARGS || '').split(' ').filter(Boolean)});
    try {
        const context=await browser.newContext({viewport:{width:1200,height:720},hasTouch:touch,deviceScaleFactor});
        const page=await context.newPage();
        const errors=[];
        page.on('pageerror',e=>errors.push(String(e)));
        await page.addInitScript(() => {
            window.gatherSounds = 0;
            const start = AudioBufferSourceNode.prototype.start;
            AudioBufferSourceNode.prototype.start = function (...args) {
                if (this.buffer && this.buffer.duration < 0.11) window.gatherSounds++;
                return start.apply(this, args);
            };
        });
        await ready(page);
        assert.equal(await page.evaluate(()=>game.mode), main?'main':'worker');
        const build=await page.locator('meta[name=ultima-build]').getAttribute('content');
        if (process.env.ULTIMA_BUILD) assert.equal(build,process.env.ULTIMA_BUILD);
        const target=await scene(page);
        const mesh='place-'+fixture.uid;
        const enabled=target=>target.evaluate(n=>BABYLON.EngineStore.LastCreatedScene.getMeshByName(n)?.isEnabled(),mesh);
        const place=page=>page.evaluate(op=>game.applyWorldOp(op),fixture);
        await place(page);
        await until(()=>enabled(target));
        await bag(page);
        await page.getByText('Mochila vazia.',{exact:true}).waitFor();
        await page.getByRole('button',{name:'Coletar',exact:true}).click();
        const click=async (point,renderer=target)=>{
            const screen=await project(renderer,point);
            await until(()=>page.evaluate(({x,y})=>document.elementFromPoint(x,y)?.id==='game-canvas',screen));
            if (touch) await page.touchscreen.tap(screen.x,screen.y);
            else await page.mouse.click(screen.x,screen.y);
            return screen;
        };
        const hover=async point=>{
            const screen=await project(target,point);
            await until(()=>page.evaluate(({x,y})=>document.elementFromPoint(x,y)?.id==='game-canvas',screen));
            await page.mouse.move(screen.x,screen.y);
        };
        if (!touch) {
            await hover({name:mesh});
            await until(async()=>(await preview(target)).objects.includes(mesh));
            assert.deepEqual(await preview(target),{objects:[mesh],ground:null});
            await hover({x:1,z:9});
            await until(async()=>(await preview(target)).objects.length===0);
            assert.equal((await preview(target)).ground,null);
            await hover({name:mesh});
            await until(async()=>(await preview(target)).objects.includes(mesh));
            await page.getByRole('button',{name:'Parar coleta',exact:true}).hover();
            await until(async()=>(await preview(target)).objects.length===0);
        }
        await target.evaluate(()=>{
            const scene=BABYLON.EngineStore.LastCreatedScene;
            const arm=scene.getTransformNodeByName('player-shoulder-right');
            globalThis.gatherMotion=false;
            const observer=scene.onAfterRenderObservable.add(()=>{
                if (arm.rotation.x < -0.40) {
                    globalThis.gatherMotion=true;
                    scene.onAfterRenderObservable.remove(observer);
                }
            });
        });
        await click({name:mesh});
        await page.getByText('+1 Toco',{exact:true}).waitFor({timeout:10000});
        await until(()=>target.evaluate(()=>globalThis.gatherMotion),'a coleta confirmada deve acionar o gesto');
        await until(()=>page.evaluate(()=>window.gatherSounds === 1), 'a marca de som da coleta não tocou');
        await until(async()=>await enabled(target)===false);
        await click({x:1,z:9});
        await page.getByText('Aproxime-se para coletar.',{exact:true}).waitFor({timeout:10000});
        await lookIntoExcavation(target);
        const soil={x:1.54,z:1.49};
        const before=(await project(target,soil)).alt;
        async function assertGroundPreview(height) {
            await hover(soil);
            await until(async()=>{
                const center=(await preview(target)).ground;
                return center && Math.hypot(center.x-soil.x,center.z-soil.z)<0.1 && Math.abs(center.y-height)<0.1;
            },'a prévia deve coincidir com o ponto de coleta');
        }
        if (!touch) await assertGroundPreview(before);
        for (let layer=1;layer<=3;layer++) {
            await click(soil);
            await until(async()=>(await records(page)).filter(r=>r.kind==='soil').length===layer);
            await until(async()=>before-(await project(target,soil)).alt>0.35*layer);
        }
        const after=(await project(target,soil)).alt;
        if (!touch) await assertGroundPreview(after);
        const saved=await records(page);
        assert.equal(saved.filter(record=>record.item==='Toco').length,1);
        assert.equal(saved.filter(record=>record.kind==='soil').length,3);
        for (const cut of saved.filter(record=>record.kind==='soil')) {
            assert.ok(Math.hypot(cut.x-soil.x,cut.z-soil.z)<0.10,'o corte deve seguir o ponto clicado');
        }
        await page.getByRole('button',{name:'Parar coleta',exact:true}).click();
        assert.deepEqual(await preview(target),{objects:[],ground:null});
        await bag(page);
        for (const item of new Set(saved.map(record=>record.item))) await page.getByText(item,{exact:true}).waitFor();
        const legacyPoint={x:-1,z:1};
        const legacyBefore=(await project(target,legacyPoint)).alt;
        await page.evaluate(point=>game.backpack.collect({id:'soil:-1,0',item:'Terra',kind:'soil',...point}),legacyPoint);
        await ready(page);
        const reloaded=await scene(page);
        await place(page);
        await until(async()=>await enabled(reloaded)===false);
        assert.ok(Math.abs((await project(reloaded,soil)).alt-after)<0.04);
        const legacyAfter=(await project(reloaded,legacyPoint)).alt;
        assert.ok(Math.abs(legacyBefore-legacyAfter-0.48)<0.04,'preserva a profundidade da escavação antiga');
        await bag(page);
        for (const item of new Set(saved.map(record=>record.item))) await page.getByText(item,{exact:true}).waitFor();
        await page.getByRole('button',{name:'Coletar',exact:true}).click();
        await lookIntoExcavation(reloaded);
        await click(legacyPoint,reloaded);
        await until(async()=>legacyAfter-(await project(reloaded,legacyPoint)).alt>0.4);
        await page.getByRole('button',{name:'Parar coleta',exact:true}).click();
        await bag(page);
        const second=await context.newPage();
        await ready(second);
        await Promise.all([page,second].map(p=>p.evaluate(r=>game.backpack.collect(r),
            {id:'concurrency-check',item:'Validação de duplicidade',kind:'object'})));
        assert.equal((await records(page)).length,7);
        await page.getByText('Validação de duplicidade',{exact:true}).waitFor();
        assert.deepEqual(errors,[]);
        console.log({build, mode:main?'main':'worker', touch, deviceScaleFactor, items:saved.map(record=>record.item),
            depth:before-after, reload:true, legacyDepth:legacyBefore-legacyAfter, atomicAcrossTabs:true, hoverTested:!touch});
    } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1});
