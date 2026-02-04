import{r as o}from"./index-D4H_InIO.js";import{a as V}from"./index-B-lxVbXh.js";import{L as s}from"./LineChart-Y5rhgTmV.js";import"./v4-CtRu48qb.js";import"./Text-C0fEdKO1.js";import"./jsx-runtime-D93ddY-N.js";import"./jsx-runtime-Dz47noOj.js";import"./Legends-DUpH8z2n.js";import"./index-CZVi18Wq.js";import"./index-Dd8bRu6S.js";import"./colors-CW2ns9F7.js";import"./CartesianChart-L8mYIb7a.js";import"./iframe-KFAmsXlB.js";const O={title:"Charts/LineChart",component:s,parameters:{layout:"padded",docs:{description:{component:"LineChart displays trend data as line or area chart with drill-through support."}}},tags:["autodocs"]},a=V("onDrillInteraction"),e=[{label:"Jan",value:120,fieldValue:"2024-01"},{label:"Feb",value:150,fieldValue:"2024-02"},{label:"Mar",value:180,fieldValue:"2024-03"},{label:"Apr",value:140,fieldValue:"2024-04"},{label:"May",value:210,fieldValue:"2024-05"},{label:"Jun",value:190,fieldValue:"2024-06"}],r={args:{data:e,title:"Cases per Month",variant:"line",onDrillInteraction:a,drillField:"month"}},t={args:{data:e,title:"Revenue Trend",variant:"area",onDrillInteraction:a,drillField:"month"}},n={args:{data:e,title:"Monthly Trend",showLegend:!0,onDrillInteraction:a,drillField:"month"}},l={args:{data:[],title:"No Data"}},i={render:()=>o.createElement("div",{style:{display:"grid",gridTemplateColumns:"1fr 1fr",gap:"24px"}},o.createElement(s,{data:e,title:"Line Variant",variant:"line",onDrillInteraction:a,drillField:"month"}),o.createElement(s,{data:e,title:"Area Variant",variant:"area",onDrillInteraction:a,drillField:"month"}))};var d,m,c;r.parameters={...r.parameters,docs:{...(d=r.parameters)==null?void 0:d.docs,source:{originalSource:`{
  args: {
    data: monthlyData,
    title: "Cases per Month",
    variant: "line",
    onDrillInteraction: handleDrill,
    drillField: "month"
  }
}`,...(c=(m=r.parameters)==null?void 0:m.docs)==null?void 0:c.source}}};var p,h,u;t.parameters={...t.parameters,docs:{...(p=t.parameters)==null?void 0:p.docs,source:{originalSource:`{
  args: {
    data: monthlyData,
    title: "Revenue Trend",
    variant: "area",
    onDrillInteraction: handleDrill,
    drillField: "month"
  }
}`,...(u=(h=t.parameters)==null?void 0:h.docs)==null?void 0:u.source}}};var D,g,v;n.parameters={...n.parameters,docs:{...(D=n.parameters)==null?void 0:D.docs,source:{originalSource:`{
  args: {
    data: monthlyData,
    title: "Monthly Trend",
    showLegend: true,
    onDrillInteraction: handleDrill,
    drillField: "month"
  }
}`,...(v=(g=n.parameters)==null?void 0:g.docs)==null?void 0:v.source}}};var y,f,C;l.parameters={...l.parameters,docs:{...(y=l.parameters)==null?void 0:y.docs,source:{originalSource:`{
  args: {
    data: [],
    title: "No Data"
  }
}`,...(C=(f=l.parameters)==null?void 0:f.docs)==null?void 0:C.source}}};var L,F,I;i.parameters={...i.parameters,docs:{...(L=i.parameters)==null?void 0:L.docs,source:{originalSource:`{
  render: () => <div style={{
    display: "grid",
    gridTemplateColumns: "1fr 1fr",
    gap: "24px"
  }}>\r
      <LineChart data={monthlyData} title="Line Variant" variant="line" onDrillInteraction={handleDrill} drillField="month" />\r
      <LineChart data={monthlyData} title="Area Variant" variant="area" onDrillInteraction={handleDrill} drillField="month" />\r
    </div>
}`,...(I=(F=i.parameters)==null?void 0:F.docs)==null?void 0:I.source}}};const j=["Default","AreaChart","WithLegend","EmptyData","Comparison"];export{t as AreaChart,i as Comparison,r as Default,l as EmptyData,n as WithLegend,j as __namedExportsOrder,O as default};
