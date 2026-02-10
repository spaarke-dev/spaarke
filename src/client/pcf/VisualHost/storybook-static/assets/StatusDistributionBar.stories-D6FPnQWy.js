import{a as N}from"./index-B-lxVbXh.js";import{S as y}from"./StatusDistributionBar-Cd8i7Oe1.js";import"./v4-CtRu48qb.js";import"./index-D4H_InIO.js";import"./Text-C0fEdKO1.js";import"./jsx-runtime-D93ddY-N.js";import"./jsx-runtime-Dz47noOj.js";const _={title:"Charts/StatusDistributionBar",component:y,parameters:{layout:"padded",docs:{description:{component:"StatusDistributionBar shows status distribution as a horizontal stacked bar."}}},tags:["autodocs"]},l=N("onDrillInteraction"),e=[{label:"Active",value:45,fieldValue:"active"},{label:"Pending",value:23,fieldValue:"pending"},{label:"On Hold",value:12,fieldValue:"onhold"},{label:"Closed",value:67,fieldValue:"closed"}],t={args:{segments:e,title:"Case Status Distribution",onDrillInteraction:l,drillField:"statuscode"}},s={args:{segments:e,title:"Status by Percentage",showCounts:!1,onDrillInteraction:l,drillField:"statuscode"}},a={args:{segments:e,title:"Larger Status Bar",height:48,onDrillInteraction:l,drillField:"statuscode"}},r={args:{segments:e,title:"Compact (No Labels)",showLabels:!1,height:24,onDrillInteraction:l,drillField:"statuscode"}},n={args:{segments:e,title:"View Only",interactive:!1}},o={args:{segments:[],title:"No Data"}};var i,c,d;t.parameters={...t.parameters,docs:{...(i=t.parameters)==null?void 0:i.docs,source:{originalSource:`{
  args: {
    segments: statusSegments,
    title: "Case Status Distribution",
    onDrillInteraction: handleDrill,
    drillField: "statuscode"
  }
}`,...(d=(c=t.parameters)==null?void 0:c.docs)==null?void 0:d.source}}};var u,m,g;s.parameters={...s.parameters,docs:{...(u=s.parameters)==null?void 0:u.docs,source:{originalSource:`{
  args: {
    segments: statusSegments,
    title: "Status by Percentage",
    showCounts: false,
    onDrillInteraction: handleDrill,
    drillField: "statuscode"
  }
}`,...(g=(m=s.parameters)==null?void 0:m.docs)==null?void 0:g.source}}};var p,D,S;a.parameters={...a.parameters,docs:{...(p=a.parameters)==null?void 0:p.docs,source:{originalSource:`{
  args: {
    segments: statusSegments,
    title: "Larger Status Bar",
    height: 48,
    onDrillInteraction: handleDrill,
    drillField: "statuscode"
  }
}`,...(S=(D=a.parameters)==null?void 0:D.docs)==null?void 0:S.source}}};var h,b,f;r.parameters={...r.parameters,docs:{...(h=r.parameters)==null?void 0:h.docs,source:{originalSource:`{
  args: {
    segments: statusSegments,
    title: "Compact (No Labels)",
    showLabels: false,
    height: 24,
    onDrillInteraction: handleDrill,
    drillField: "statuscode"
  }
}`,...(f=(b=r.parameters)==null?void 0:b.docs)==null?void 0:f.source}}};var I,v,w;n.parameters={...n.parameters,docs:{...(I=n.parameters)==null?void 0:I.docs,source:{originalSource:`{
  args: {
    segments: statusSegments,
    title: "View Only",
    interactive: false
  }
}`,...(w=(v=n.parameters)==null?void 0:v.docs)==null?void 0:w.source}}};var C,F,L;o.parameters={...o.parameters,docs:{...(C=o.parameters)==null?void 0:C.docs,source:{originalSource:`{
  args: {
    segments: [],
    title: "No Data"
  }
}`,...(L=(F=o.parameters)==null?void 0:F.docs)==null?void 0:L.source}}};const k=["Default","ShowPercentages","TallBar","NoLabels","NonInteractive","EmptyData"];export{t as Default,o as EmptyData,r as NoLabels,n as NonInteractive,s as ShowPercentages,a as TallBar,k as __namedExportsOrder,_ as default};
