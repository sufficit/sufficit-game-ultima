// Fixture local ao navegador. Requer playwright-core e Chromium.
// ULTIMA_SITE pode apontar para um publish local. ULTIMA_MODE=main exercita o fallback.
const {chromium}=require('playwright-core');
const assert=require('node:assert/strict');
const site=process.env.ULTIMA_SITE || 'https://sufficit.github.io/sufficit-game-ultima/';
const main=process.env.ULTIMA_MODE==='main';
const fixture={kind:'place',uid:'browser-tree',asset:'flora.pine',x:0,z:2,rot:0,scale:1};
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
async function wait(callback,label) {
    for(let i=0;i<100;i++){
        const v=await callback();
        if(v) return v;
        await sleep(250);
    }
    throw Error(label);
}
(async()=>{
 const browser=await chromium.launch({executablePath:process.env.ULTIMA_CHROMIUM || undefined,
    args:(process.env.ULTIMA_CHROMIUM_ARGS || '').split(' ').filter(Boolean)});
 try{
    const p=await browser.newPage({viewport:{width:1200,height:720}});
    const errors=[];
    p.on('pageerror',e=>errors.push(String(e)));
    const url=site+'?live=0&rede=0&api=http%3A%2F%2F127.0.0.1%3A19999'+(main?'&render=main&gpu=0':'');
    await p.goto(url,{waitUntil:'domcontentloaded'});
    const build=await p.locator('meta[name=ultima-build]').getAttribute('content');
    if(process.env.ULTIMA_BUILD) assert.equal(build, process.env.ULTIMA_BUILD);
    await wait(()=>p.evaluate(async()=>{
        if(!window.game?.glb?.status || !window.game?.build?.state) return false;
        const map=await game.build.state(), glb=await game.glb.status();
        return map?.chunks>=25 && glb?.loaded.some(id=>id.startsWith('flora.pine')) &&
            glb?.loaded.some(id=>id.startsWith('prop.stump')) &&
            glb?.loaded.some(id=>id.startsWith('prop.log')) &&
            glb?.loaded.some(id=>id.startsWith('rock.boulder2')) && (await game.models.status()).queue===0;
    }),'mundo carregado');
    assert.equal(await p.evaluate(()=>game.mode),main?'main':'worker');
    await p.getByRole('button',{name:'detalhes',exact:true}).click();
    await p.locator('#game-canvas').focus();
    const pose=async()=> {
        if(main) return p.evaluate(()=>{
            const {x,y,z}=BABYLON.EngineStore.LastCreatedScene.getTransformNodeByName('player').position;
            return {x,y,z};
        });
        const status=await p.locator('.ultima-status').innerText();
        const match=/pos \((-?[\d.,]+), (-?[\d.,]+)\) · alt (-?[\d.,]+)/.exec(status);
        assert.ok(match, status);
        return {x:Number(match[1].replace(',','.')),z:Number(match[2].replace(',','.')),y:Number(match[3].replace(',','.'))};
    };
    const apply=op=>p.evaluate(op=>game.applyWorldOp(op),op);
    async function walk(keys,ms){
        for(const key of keys) await p.keyboard.down(key);
        await p.waitForTimeout(ms);
        for(const key of keys) await p.keyboard.up(key);
        await p.waitForTimeout(300);
        if(errors.length) throw Error(errors.join('\n'));
        return pose();
    }
    await apply(fixture);
    await p.waitForTimeout(300);
    const blocked=await walk(['w'],1150);
    console.log(JSON.stringify({build,mode:main?'main':'worker',blocked}));
    assert.ok(blocked.z>1.28 && blocked.z<1.61,'o tronco deve parar o jogador em z≈1,47');
    await p.locator('.ultima-hint').getByText('para examinar',{exact:false}).waitFor();
    await p.keyboard.press('e');
    await p.getByText('Você examinou: Pinheiro',{exact:true}).waitFor();
    const slide=await walk(['w','d'],450);
    assert.ok(slide.x>0.45 && slide.z>2, 'desliza pela lateral '+JSON.stringify(slide));
    await apply({kind:'remove',uid:fixture.uid});
    await p.evaluate(()=>game.resetPlayer());
    await p.waitForTimeout(300);
    const free=await walk(['w'],820);
    assert.ok(free.z>2.65, 'remoção libera o caminho');
    await p.evaluate(()=>game.resetPlayer());
    await apply({...fixture,uid:'browser-soft',asset:'flora.fern'});
    await p.waitForTimeout(300);
    const soft=await walk(['w'],850);
    assert.ok(soft.z>2.65,'samambaia permite passagem');
    await apply({kind:'remove',uid:'browser-soft'});
    await p.evaluate(()=>game.resetPlayer());
    await apply({...fixture,uid:'browser-stump',asset:'prop.stump',scale:1.45});
    await p.waitForTimeout(300);
    const stump=await walk(['w'],850);
    assert.ok(stump.z>0.7 && stump.z<1.55,'toco sólido '+JSON.stringify(stump));
    await apply({kind:'remove',uid:'browser-stump'});
    await p.evaluate(()=>game.resetPlayer());
    await apply({...fixture,uid:'browser-rock',asset:'rock.boulder2'});
    await p.waitForTimeout(300);
    const rock=await walk(['w'],850);
    assert.ok(rock.z>0.45 && rock.z<1.7,'pedra sólida '+JSON.stringify(rock));
    await apply({kind:'remove',uid:'browser-rock'});

    await p.evaluate(()=>game.resetPlayer());
    await apply({...fixture,uid:'browser-log',asset:'prop.log',scale:0.56});
    await p.waitForTimeout(400);
    const renderer=main ? p : p.workers()[0];
    // Registra cada quadro no renderizador para captar o apoio curto em corrida.
    await renderer.evaluate(()=>{
        const scene=BABYLON.EngineStore.LastCreatedScene;
        const player=scene.getTransformNodeByName('player');
        const knees=['left','right'].map(side=>scene.getTransformNodeByName('player-knee-'+side));
        const ankles=['left','right'].map(side=>scene.getTransformNodeByName('player-ankle-'+side));
        const samples=[];
        const observer=scene.onAfterRenderObservable.add(()=>{
            const {z}=player.position;
            const heights=ankles.map(ankle=>ankle.getAbsolutePosition().y);
            samples.push({z,ankleY:Math.max(...heights),bend:Math.max(...knees.map(knee=>knee.rotation.x)),
                feetDifference:Math.abs(heights[0]-heights[1])});
        });
        globalThis.stopStepTrace=()=>{
            scene.onAfterRenderObservable.remove(observer);
            return samples;
        };
    });
    await p.keyboard.down('w');
    try {
        await p.waitForTimeout(1050);
    } finally {
        await p.keyboard.up('w');
    }
    await p.waitForTimeout(300);
    const samples=await renderer.evaluate(()=>globalThis.stopStepTrace());
    const pastLog=await pose();
    const overLog=samples.filter(sample=>sample.z>1.4 && sample.z<2.6);
    const step={
        anklePeak:Math.max(...overLog.map(sample=>sample.ankleY)),
        flex:Math.max(...overLog.map(sample=>sample.bend)),
        feetDifference:Math.max(...overLog.map(sample=>sample.feetDifference)),
        end:pastLog,
    };
    console.log(JSON.stringify({step}));
    assert.ok(step.anklePeak>0.28 && pastLog.z>3.2 && pastLog.y<0.19,'a perna sobe sobre o tronco e o jogador volta ao chão');
    assert.ok(step.flex>0.55 && step.feetDifference>0.10,'joelho flexiona e os pés alternam apoio');
    {
        const contact = await renderer.evaluate(async () => {
            const scene = BABYLON.EngineStore.LastCreatedScene;
            const player = scene.getTransformNodeByName('player');
            const log = scene.getMeshByName('place-browser-log');
            const shoes = ['left', 'right'].map(side => scene.getMeshByName('player-shoe-' + side));
            const down = new BABYLON.Vector3(0, -1, 0);
            const samples = [];
            player.rotation.y = 0;
            for (const z of [1.7, 1.8, 1.9, 2, 2.1, 2.2, 2.3]) {
                player.position.set(0, player.position.y, z);
                await new Promise(resolve => scene.onAfterRenderObservable.addOnce(() =>
                    scene.onAfterRenderObservable.addOnce(() => resolve())));
                const feet = shoes.map(shoe => {
                    const { centerWorld, minimumWorld } = shoe.getBoundingInfo().boundingBox;
                    const ray = new BABYLON.Ray(new BABYLON.Vector3(centerWorld.x, 3, centerWorld.z), down);
                    const bark = ray.intersectsMesh(log).pickedPoint;
                    const floor = scene.pickWithRay(ray, mesh => mesh.name.startsWith('ground-')).pickedPoint;
                    return { supportedByLog: !!bark, gap: minimumWorld.y - Math.max(floor.y, bark?.y ?? floor.y) };
                });
                samples.push({ z, feet });
            }
            return samples;
        });
        console.log(JSON.stringify({contact}));
        assert.ok(contact.some(sample => sample.feet.every(foot => foot.supportedByLog)), 'os pés chegam ao topo do tronco');
        assert.ok(contact.every(sample => sample.feet.every(foot => Math.abs(foot.gap)<0.04)),
            'cada sola deve ficar a menos de 4 cm do tronco ou do chão, mesmo nas bordas');
    }
    await apply({kind:'remove',uid:'browser-log'});
    await p.evaluate(()=>game.resetPlayer());
    await apply({...fixture,uid:'browser-high-log',asset:'prop.log',scale:0.95});
    await p.waitForTimeout(300);
    const highLog=await walk(['w'],850);
    assert.ok(highLog.z>0.5 && highLog.z<1.55 && highLog.y<0.19,'tronco acima do joelho bloqueia');
    await apply({kind:'remove',uid:'browser-high-log'});

    let nativeTree;
    if(main) {
        const tree=await p.evaluate(()=>{
            const s=BABYLON.EngineStore.LastCreatedScene;
            const player=s.getTransformNodeByName('player');
            const tree=s.meshes.filter(m=>m.name.startsWith('tree-tree.pine-trunk-')&&m.isEnabled()&&Math.abs(m.position.x)>6)
                .sort((a,b)=>a.position.lengthSquared()-b.position.lengthSquared())[0];
            if(!tree) return null;
            player.position.set(tree.position.x,tree.position.y+0.25,tree.position.z-1.15);
            return {x:tree.position.x,z:tree.position.z};
        });
        assert.ok(tree,'tronco DLL');
        await p.waitForTimeout(300);
        nativeTree=await walk(['w'],480);
        assert.ok(Math.abs(nativeTree.x-tree.x)<0.1 && nativeTree.z<tree.z-0.38 && nativeTree.z>tree.z-0.95,'pinheiro do cenário bloqueia '+JSON.stringify({nativeTree,tree}));
    }
    await p.evaluate(()=>game.teleportToGuardian());
    await p.waitForTimeout(500);
    await p.keyboard.press('e');
    await p.locator('.ultima-dialog').waitFor();
    assert.deepEqual(errors,[]);
    console.log(JSON.stringify({slide,free,soft,stump,rock,highLog,nativeTree,dialog:true}));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1});
