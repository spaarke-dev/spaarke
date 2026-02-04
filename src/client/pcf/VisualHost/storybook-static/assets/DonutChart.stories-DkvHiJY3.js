import{a as S}from"./index-B-lxVbXh.js";import{D as v}from"./DonutChart-BEPSkMiT.js";import"./v4-CtRu48qb.js";import"./index-D4H_InIO.js";import"./Text-C0fEdKO1.js";import"./jsx-runtime-D93ddY-N.js";import"./jsx-runtime-Dz47noOj.js";import"./Legends-DUpH8z2n.js";import"./index-CZVi18Wq.js";import"./index-Dd8bRu6S.js";import"./gradients-B-09CRJk.js";import"./colors-CW2ns9F7.js";import"./ChartHoverCard-ScuzKI_J.js";const _={title:"Charts/DonutChart",component:v,parameters:{layout:"centered",docs:{description:{component:"DonutChart displays proportional data with drill-through support."}}},tags:["autodocs"]},o=S("onDrillInteraction"),n=[{label:"Open",value:45,fieldValue:"open"},{label:"In Progress",value:32,fieldValue:"inprogress"},{label:"Pending",value:18,fieldValue:"pending"},{label:"Resolved",value:89,fieldValue:"resolved"}],t={args:{data:n,title:"Matters by Status",onDrillInteraction:o,drillField:"statuscode"}},a={args:{data:n,title:"Distribution (Pie)",innerRadius:0,onDrillInteraction:o,drillField:"statuscode"}},e={args:{data:n,title:"Total Cases",centerLabel:"184 Total",onDrillInteraction:o,drillField:"statuscode"}},r={args:{data:n,title:"Compact View",showLegend:!1,height:200,onDrillInteraction:o,drillField:"statuscode"}},s={args:{data:[],title:"No Data"}};var l,i,d;t.parameters={...t.parameters,docs:{...(l=t.parameters)==null?void 0:l.docs,source:{originalSource:`{
  args: {
    data: statusData,
    title: "Matters by Status",
    onDrillInteraction: handleDrill,
    drillField: "statuscode"
  }
}`,...(d=(i=t.parameters)==null?void 0:i.docs)==null?void 0:d.source}}};var c,u,p;a.parameters={...a.parameters,docs:{...(c=a.parameters)==null?void 0:c.docs,source:{originalSource:`{
  args: {
    data: statusData,
    title: "Distribution (Pie)",
    innerRadius: 0,
    onDrillInteraction: handleDrill,
    drillField: "statuscode"
  }
}`,...(p=(u=a.parameters)==null?void 0:u.docs)==null?void 0:p.source}}};var m,D,g;e.parameters={...e.parameters,docs:{...(m=e.parameters)==null?void 0:m.docs,source:{originalSource:`{
  args: {
    data: statusData,
    title: "Total Cases",
    centerLabel: "184 Total",
    onDrillInteraction: handleDrill,
    drillField: "statuscode"
  }
}`,...(g=(D=e.parameters)==null?void 0:D.docs)==null?void 0:g.source}}};var h,C,f;r.parameters={...r.parameters,docs:{...(h=r.parameters)==null?void 0:h.docs,source:{originalSource:`{
  args: {
    data: statusData,
    title: "Compact View",
    showLegend: false,
    height: 200,
    onDrillInteraction: handleDrill,
    drillField: "statuscode"
  }
}`,...(f=(C=r.parameters)==null?void 0:C.docs)==null?void 0:f.source}}};var b,I,F;s.parameters={...s.parameters,docs:{...(b=s.parameters)==null?void 0:b.docs,source:{originalSource:`{
  args: {
    data: [],
    title: "No Data"
  }
}`,...(F=(I=s.parameters)==null?void 0:I.docs)==null?void 0:F.source}}};const j=["Default","PieChart","WithCustomCenter","NoLegend","EmptyData"];export{t as Default,s as EmptyData,r as NoLegend,a as PieChart,e as WithCustomCenter,j as __namedExportsOrder,_ as default};
