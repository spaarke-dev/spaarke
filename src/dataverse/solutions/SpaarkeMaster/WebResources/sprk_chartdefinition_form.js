/**
 * Chart Definition Form JavaScript
 * Syncs lookup selections to backing text fields for Visual Host PCF.
 * Web Resource: sprk_/scripts/chartdefinition_form.js
 */
var Spaarke = Spaarke || {};
Spaarke.ChartDefinition = {
  onLoad: function(executionContext) {
    var formContext = executionContext.getFormContext();
    formContext.getAttribute("sprk_reportingentity").addOnChange(Spaarke.ChartDefinition.onReportingEntityChange);
    formContext.getAttribute("sprk_reportingview").addOnChange(Spaarke.ChartDefinition.onReportingViewChange);
  },

  onReportingEntityChange: function(executionContext) {
    var formContext = executionContext.getFormContext();
    var lookup = formContext.getAttribute("sprk_reportingentity").getValue();
    formContext.getAttribute("sprk_reportingview").setValue(null);
    formContext.getAttribute("sprk_baseviewid").setValue(null);
    if (lookup && lookup[0]) {
      Xrm.WebApi.retrieveRecord("sprk_reportingentity", lookup[0].id, "?$select=sprk_logicalname").then(
        function(result) { formContext.getAttribute("sprk_entitylogicalname").setValue(result.sprk_logicalname); },
        function(error) { console.error("Failed to retrieve reporting entity:", error.message); }
      );
    } else {
      formContext.getAttribute("sprk_entitylogicalname").setValue(null);
    }
  },

  onReportingViewChange: function(executionContext) {
    var formContext = executionContext.getFormContext();
    var lookup = formContext.getAttribute("sprk_reportingview").getValue();
    if (lookup && lookup[0]) {
      Xrm.WebApi.retrieveRecord("sprk_reportingview", lookup[0].id, "?$select=sprk_viewid").then(
        function(result) { formContext.getAttribute("sprk_baseviewid").setValue(result.sprk_viewid); },
        function(error) { console.error("Failed to retrieve reporting view:", error.message); }
      );
    } else {
      formContext.getAttribute("sprk_baseviewid").setValue(null);
    }
  }
};
