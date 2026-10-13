// sprk.document.commands.js
function sprk_OpenDocumentQuickCreate(primaryControl) {
  // If coming from Associated Grid, try to pick up parent
  let parentRef = null;
  try {
    parentRef = primaryControl?._entityReference || primaryControl?.getFormContext?.()?.data?.entity?.getEntityReference?.();
  } catch(e){}

  const pageInput = {
    pageType: "customPage",
    name: "sprk_documentuploaddialog_e52db", // your Custom Page name
    parameters: {
      parentEntityName: parentRef?.entityType || null,
      parentRecordId: parentRef?.id || null,
      // pass anything your PCF expects (e.g., return target, pane/dialog mode)
      mode: "dialog"
    }
  };

  const options = {
    target: 2,                 // 2 = dialog
    position: 1,               // centered dialog
    title: "New Document",
    width: { value: 600, unit: "px" }
  };

  return Xrm.Navigation.navigateTo(pageInput, options).then(() => {
    // After dialog closes, refresh the grid so new Documents appear
    try { primaryControl.getGrid().refresh(); } catch(e){}
  });
}