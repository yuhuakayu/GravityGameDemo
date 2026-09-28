const path = require('path');
const sharp = require('sharp');

// Imagegen supplies the standing/floating drawings. Keep a fixed pose layer and
// translate its parts for animation so the face, helmet and planted feet never
// morph between independently generated concept frames.
const palette = ['181726','444454','686B80','8E90A4','C8CBD8','F4F4F8',
  'F28AA0','C45C7A','FFC4D0','AAD7E6','FFFFFF','3FD6E0',
  'F6D9CC','DEA3A5','5E70C8','2D3C73'].map(hex => [0,2,4].map(i => parseInt(hex.slice(i,i+2),16)));
const width = 96, height = 112, scale = 8;
const output = __dirname;

function translateRegion(frame, source, box, dx, dy) {
  const [left,top,right,bottom]=box;
  for(let y=top;y<bottom;y++)for(let x=left;x<right;x++)frame.fill(0,(y*width+x)*4,(y*width+x)*4+4);
  for(let y=top;y<bottom;y++)for(let x=left;x<right;x++){
    const from=(y*width+x)*4,to=((y+dy)*width+x+dx)*4;
    if(source[from+3])source.copy(frame,to,from,from+4);
  }
}

function hairTip(frame, box, dx, dy) {
  const source=Buffer.from(frame),[left,top,right,bottom]=box;
  const pixels=[];
  for(let y=top;y<bottom;y++)for(let x=left;x<right;x++){
    const i=(y*width+x)*4;
    if(source[i+3] && source[i]>150 && source[i]>source[i+1]*1.25)pixels.push([x,y]);
  }
  for(const [x,y] of pixels)frame.set([196,92,122,255],(y*width+x)*4);
  for(const [x,y] of pixels){const i=(y*width+x)*4,to=((y+dy)*width+x+dx)*4;source.copy(frame,to,i,i+4);}
}

function animate(base,name) {
  const frames=[];
  for(let i=0;i<(name==='player_idle'?4:6);i++) {
    const f=Buffer.from(base);
    if(name==='player_idle') {
      if(i===1||i===2) {
        translateRegion(f,base,[0,1,width,64],0,-1);
        base.copy(f,63*width*4,63*width*4,64*width*4);
      }
      if(i===2) {
        const source=Buffer.from(f);
        translateRegion(f,source,[43,72,51,81],-1,0);
        translateRegion(f,source,[68,71,75,80],1,0);
      }
      if(i===3)hairTip(f,[49,41,55,45],0,1);
    } else {
      // Stagger the small hand, boot and hair motions around a one-pixel bob.
      if(i===2||i===3) {
        translateRegion(f,base,[18,42,34,51],0,-1);
        translateRegion(f,base,[67,52,80,61],0,-1);
      }
      if(i===2) {
        translateRegion(f,base,[34,79,44,90],0,1);
        base.copy(f,(79*width+34)*4,(79*width+34)*4,(79*width+44)*4);
      }
      if(i===4) {
        translateRegion(f,base,[43,90,58,99],0,1);
        base.copy(f,(90*width+43)*4,(90*width+43)*4,(90*width+58)*4);
      }
      if(i===1||i===2)hairTip(f,[52,31,59,35],0,-1);
      if(i===3)hairTip(f,[52,31,59,35],1,0);
      if(i===5)hairTip(f,[52,31,59,35],-1,0);
      if(i===1||i===2) {
        const source=Buffer.from(f);f.fill(0);
        translateRegion(f,source,[0,1,width,height],0,-1);
      }
    }
    frames.push(f);
  }
  return frames;
}

function indexed(data) {
  const result = Buffer.from(data);
  for (let i = 0; i < result.length; i += 4) {
    if (result[i+3] < 128) { result.fill(0,i,i+4); continue; }
    let best = palette[0], distance = Infinity;
    for (const colour of palette) {
      const d = colour.reduce((sum, value, k) => sum + (value-result[i+k]) ** 2, 0);
      if (d < distance) { distance = d; best = colour; }
    }
    result.set([...best,255],i);
  }
  return result;
}

async function build(name, cols, rows, ratio, delay) {
  const sourcePath = path.join(output,`${name}_concept.png`);
  const {data,info} = await sharp(sourcePath).ensureAlpha().raw().toBuffer({resolveWithObject:true});
  let frames = [];
  for (let index = 0; index < cols*rows; index++) {
    const col=index%cols,row=Math.floor(index/cols);
    const x0=Math.round(col*info.width/cols),x1=Math.round((col+1)*info.width/cols);
    const y0=Math.round(row*info.height/rows),y1=Math.round((row+1)*info.height/rows);
    let minX=x1,maxX=x0,minY=y1,maxY=y0;
    for(let y=y0;y<y1;y++) for(let x=x0;x<x1;x++) if(data[(y*info.width+x)*4+3]>=128){
      minX=Math.min(minX,x);maxX=Math.max(maxX,x);minY=Math.min(minY,y);maxY=Math.max(maxY,y);
    }
    // Align all frames using the helmet centre, independent of arm extension.
    let headMin=x1,headMax=x0;
    for(let y=minY;y<minY+Math.round((maxY-minY)*.24);y++)for(let x=minX;x<=maxX;x++)
      if(data[(y*info.width+x)*4+3]>=128){headMin=Math.min(headMin,x);headMax=Math.max(headMax,x);}
    const w=Math.round((maxX-minX+1)*ratio),h=Math.round((maxY-minY+1)*ratio);
    const pixels=indexed(await sharp(sourcePath).extract({left:minX,top:minY,width:maxX-minX+1,height:maxY-minY+1})
      .resize(w,h,{kernel:'nearest'}).ensureAlpha().raw().toBuffer());
    const frame=Buffer.alloc(width*height*4);
    const left=Math.round(58-((headMin+headMax)*.5-minX)*ratio);
    const top=name==='player_idle'?height-h:10+([0,-1,-1,0,0,0][index]);
    for(let y=0;y<h;y++)for(let x=0;x<w;x++){
      const dx=left+x,dy=top+y;
      if(dx<0||dx>=width||dy<0||dy>=height)throw new Error(`${name} frame ${index}: clipped pixel`);
      pixels.copy(frame,(dy*width+dx)*4,(y*w+x)*4,(y*w+x)*4+4);
    }
    frames.push(frame);
  }
  frames=animate(frames[0],name);
  const sheet=Buffer.alloc(width*frames.length*height*4);
  frames.forEach((frame,i)=>{for(let y=0;y<height;y++)frame.copy(sheet,(y*width*frames.length+i*width)*4,y*width*4,(y+1)*width*4);});
  await sharp(sheet,{raw:{width:width*frames.length,height,channels:4}}).png().toFile(path.join(output,`${name}.png`));
  await sharp(sheet,{raw:{width:width*frames.length,height,channels:4}}).resize(width*frames.length*4,height*4,{kernel:'nearest'}).png().toFile(path.join(output,`${name}_sheet_4x.png`));
  const enlarged=await Promise.all(frames.map(frame=>sharp(frame,{raw:{width,height,channels:4}}).resize(width*scale,height*scale,{kernel:'nearest'}).raw().toBuffer()));
  const gifPath=path.join(output,`${name}_preview_8x.gif`);
  await sharp(Buffer.concat(enlarged),{raw:{width:width*scale,height:height*scale*frames.length,channels:4,pageHeight:height*scale}})
    .gif({loop:0,delay:Array(frames.length).fill(delay),colours:32,dither:0,interFrameMaxError:0,interPaletteMaxError:0,keepDuplicateFrames:true}).toFile(gifPath);
  const metadata=await sharp(gifPath,{animated:true}).metadata();
  const unique=new Set();let translucent=0;
  for(let i=0;i<sheet.length;i+=4){if(sheet[i+3]===255)unique.add(sheet.subarray(i,i+3).toString('hex'));else if(sheet[i+3]!==0)translucent++;}
  console.log(JSON.stringify({name,frameSize:[width,height],frames:metadata.pages,delay:metadata.delay,loop:metadata.loop,opaqueColours:unique.size,translucentPixels:translucent}));
}

(async()=>{
  await build('player_idle',4,1,1/8,200);
  await build('player_float',3,2,1/5,120);
})().catch(error=>{console.error(error);process.exitCode=1;});
