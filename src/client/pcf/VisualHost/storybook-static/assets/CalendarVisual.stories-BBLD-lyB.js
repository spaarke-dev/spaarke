import{a as C}from"./index-B-lxVbXh.js";import{C as E}from"./CalendarVisual-Y3fEVNV0.js";import"./v4-CtRu48qb.js";import"./index-D4H_InIO.js";import"./Text-C0fEdKO1.js";import"./jsx-runtime-D93ddY-N.js";import"./jsx-runtime-Dz47noOj.js";import"./useARIAButtonProps-CqzD7EOT.js";import"./useIsomorphicLayoutEffect-DxaFQ3i0.js";import"./keys-BStMjYrg.js";import"./__resetStyles.esm-CYXmtSuc.js";import"./createFluentIcon-D98y1lfk.js";import"./IconDirectionContext-Dwe_X4OL.js";const z={title:"Charts/CalendarVisual",component:E,parameters:{layout:"padded",docs:{description:{component:"CalendarVisual displays a monthly calendar grid with event indicators."}}},tags:["autodocs"]},l=C("onDrillInteraction"),d=()=>{const n=new Date,t=n.getFullYear(),e=n.getMonth();return[{date:new Date(t,e,3),count:2},{date:new Date(t,e,7),count:5},{date:new Date(t,e,12),count:1},{date:new Date(t,e,15),count:8},{date:new Date(t,e,18),count:3},{date:new Date(t,e,22),count:4},{date:new Date(t,e,25),count:2},{date:new Date(t,e,28),count:6}]},a={args:{events:d(),title:"Deadlines This Month",onDrillInteraction:l,drillField:"duedate"}},r={args:{events:d(),title:"Current Month Only",showNavigation:!1,onDrillInteraction:l,drillField:"duedate"}},o={args:{events:[],title:"No Events",onDrillInteraction:l,drillField:"duedate"}},s={args:{events:(()=>{const n=new Date,t=[];for(let e=1;e<=28;e++)e%2===0&&t.push({date:new Date(n.getFullYear(),n.getMonth(),e),count:Math.floor(Math.random()*10)+1});return t})(),title:"Busy Month",onDrillInteraction:l,drillField:"duedate"}},i={args:{events:d(),title:"View Only",showNavigation:!0}};var c,u,m;a.parameters={...a.parameters,docs:{...(c=a.parameters)==null?void 0:c.docs,source:{originalSource:`{
  args: {
    events: generateEvents(),
    title: "Deadlines This Month",
    onDrillInteraction: handleDrill,
    drillField: "duedate"
  }
}`,...(m=(u=a.parameters)==null?void 0:u.docs)==null?void 0:m.source}}};var p,g,D;r.parameters={...r.parameters,docs:{...(p=r.parameters)==null?void 0:p.docs,source:{originalSource:`{
  args: {
    events: generateEvents(),
    title: "Current Month Only",
    showNavigation: false,
    onDrillInteraction: handleDrill,
    drillField: "duedate"
  }
}`,...(D=(g=r.parameters)==null?void 0:g.docs)==null?void 0:D.source}}};var h,v,w;o.parameters={...o.parameters,docs:{...(h=o.parameters)==null?void 0:h.docs,source:{originalSource:`{
  args: {
    events: [],
    title: "No Events",
    onDrillInteraction: handleDrill,
    drillField: "duedate"
  }
}`,...(w=(v=o.parameters)==null?void 0:v.docs)==null?void 0:w.source}}};var y,f,M;s.parameters={...s.parameters,docs:{...(y=s.parameters)==null?void 0:y.docs,source:{originalSource:`{
  args: {
    events: (() => {
      const today = new Date();
      const events: ICalendarEvent[] = [];
      for (let i = 1; i <= 28; i++) {
        if (i % 2 === 0) {
          events.push({
            date: new Date(today.getFullYear(), today.getMonth(), i),
            count: Math.floor(Math.random() * 10) + 1
          });
        }
      }
      return events;
    })(),
    title: "Busy Month",
    onDrillInteraction: handleDrill,
    drillField: "duedate"
  }
}`,...(M=(f=s.parameters)==null?void 0:f.docs)==null?void 0:M.source}}};var I,N,F;i.parameters={...i.parameters,docs:{...(I=i.parameters)==null?void 0:I.docs,source:{originalSource:`{
  args: {
    events: generateEvents(),
    title: "View Only",
    showNavigation: true
  }
}`,...(F=(N=i.parameters)==null?void 0:N.docs)==null?void 0:F.source}}};const A=["Default","NoNavigation","EmptyCalendar","HighDensity","NonInteractive"];export{a as Default,o as EmptyCalendar,s as HighDensity,r as NoNavigation,i as NonInteractive,A as __namedExportsOrder,z as default};
