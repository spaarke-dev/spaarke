// infrastructure/bicep/modules/dashboard.bicep
// Azure Dashboard for AI feature monitoring

@description('Name of the dashboard')
param dashboardName string

@description('Location for the dashboard')
param location string = resourceGroup().location

@description('Application Insights resource ID')
param appInsightsId string

@description('Log Analytics workspace ID (reserved for future log queries)')
#disable-next-line no-unused-params
param logAnalyticsWorkspaceId string

@description('Tags for the resources')
param tags object = {}

// Dashboard resource
resource dashboard 'Microsoft.Portal/dashboards@2020-09-01-preview' = {
  name: dashboardName
  location: location
  tags: union(tags, {
    'hidden-title': 'SDAP AI Monitoring Dashboard'
  })
  properties: {
    lenses: [
      {
        order: 0
        parts: [
          // =====================================================
          // Row 1: Overview Tiles
          // =====================================================
          {
            position: {
              x: 0
              y: 0
              rowSpan: 2
              colSpan: 3
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MonitorChartPart'
              inputs: [
                {
                  name: 'options'
                  value: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: appInsightsId
                          }
                          name: 'customMetrics/ai.summarize.requests'
                          aggregationType: 1 // Sum
                          namespace: 'microsoft.insights/components'
                          metricVisualization: {
                            displayName: 'Total AI Requests'
                            color: '#0078D4'
                          }
                        }
                      ]
                      title: 'Total AI Requests'
                      titleKind: 1
                      visualization: {
                        chartType: 2 // Line
                        legendVisualization: {
                          isVisible: true
                          position: 2 // Bottom
                          hideSubtitle: false
                        }
                      }
                      timespan: {
                        relative: {
                          duration: 86400000 // 24 hours
                        }
                      }
                    }
                  }
                }
              ]
            }
          }
          {
            position: {
              x: 3
              y: 0
              rowSpan: 2
              colSpan: 3
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MonitorChartPart'
              inputs: [
                {
                  name: 'options'
                  value: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: appInsightsId
                          }
                          name: 'customMetrics/ai.summarize.successes'
                          aggregationType: 1
                          namespace: 'microsoft.insights/components'
                          metricVisualization: {
                            displayName: 'Successes'
                            color: '#107C10'
                          }
                        }
                        {
                          resourceMetadata: {
                            id: appInsightsId
                          }
                          name: 'customMetrics/ai.summarize.failures'
                          aggregationType: 1
                          namespace: 'microsoft.insights/components'
                          metricVisualization: {
                            displayName: 'Failures'
                            color: '#D13438'
                          }
                        }
                      ]
                      title: 'Success vs Failure'
                      titleKind: 1
                      visualization: {
                        chartType: 2
                        legendVisualization: {
                          isVisible: true
                          position: 2
                          hideSubtitle: false
                        }
                      }
                      timespan: {
                        relative: {
                          duration: 86400000
                        }
                      }
                    }
                  }
                }
              ]
            }
          }
          {
            position: {
              x: 6
              y: 0
              rowSpan: 2
              colSpan: 3
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MonitorChartPart'
              inputs: [
                {
                  name: 'options'
                  value: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: appInsightsId
                          }
                          name: 'customMetrics/circuit_breaker.open_count'
                          aggregationType: 3 // Max
                          namespace: 'microsoft.insights/components'
                          metricVisualization: {
                            displayName: 'Open Circuits'
                            color: '#D13438'
                          }
                        }
                      ]
                      title: 'Open Circuit Breakers'
                      titleKind: 1
                      visualization: {
                        chartType: 2
                        legendVisualization: {
                          isVisible: true
                          position: 2
                          hideSubtitle: false
                        }
                      }
                      timespan: {
                        relative: {
                          duration: 86400000
                        }
                      }
                    }
                  }
                }
              ]
            }
          }
          {
            position: {
              x: 9
              y: 0
              rowSpan: 2
              colSpan: 3
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MonitorChartPart'
              inputs: [
                {
                  name: 'options'
                  value: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: appInsightsId
                          }
                          name: 'customMetrics/ai.summarize.tokens'
                          aggregationType: 1
                          namespace: 'microsoft.insights/components'
                          metricVisualization: {
                            displayName: 'Tokens Used'
                            color: '#8764B8'
                          }
                        }
                      ]
                      title: 'Total Tokens Used'
                      titleKind: 1
                      visualization: {
                        chartType: 2
                        legendVisualization: {
                          isVisible: true
                          position: 2
                          hideSubtitle: false
                        }
                      }
                      timespan: {
                        relative: {
                          duration: 86400000
                        }
                      }
                    }
                  }
                }
              ]
            }
          }

          // =====================================================
          // Row 2: RAG Performance
          // =====================================================
          {
            position: {
              x: 0
              y: 2
              rowSpan: 1
              colSpan: 12
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MarkdownPart'
              inputs: []
              settings: {
                content: {
                  settings: {
                    content: '## RAG Performance\nLatency, throughput, and cache metrics for hybrid search operations'
                  }
                }
              }
            }
          }
          {
            position: {
              x: 0
              y: 3
              rowSpan: 3
              colSpan: 4
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MonitorChartPart'
              inputs: [
                {
                  name: 'options'
                  value: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: appInsightsId
                          }
                          name: 'customMetrics/ai.rag.duration'
                          aggregationType: 4 // Average
                          namespace: 'microsoft.insights/components'
                          metricVisualization: {
                            displayName: 'Avg Duration'
                            color: '#0078D4'
                          }
                        }
                      ]
                      title: 'RAG Search Latency (ms)'
                      titleKind: 1
                      visualization: {
                        chartType: 2
                        legendVisualization: {
                          isVisible: true
                          position: 2
                          hideSubtitle: false
                        }
                      }
                      timespan: {
                        relative: {
                          duration: 86400000
                        }
                      }
                    }
                  }
                }
              ]
            }
          }
          {
            position: {
              x: 4
              y: 3
              rowSpan: 3
              colSpan: 4
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MonitorChartPart'
              inputs: [
                {
                  name: 'options'
                  value: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: appInsightsId
                          }
                          name: 'customMetrics/ai.rag.requests'
                          aggregationType: 1
                          namespace: 'microsoft.insights/components'
                          metricVisualization: {
                            displayName: 'RAG Requests'
                            color: '#00BCF2'
                          }
                        }
                      ]
                      title: 'RAG Throughput'
                      titleKind: 1
                      visualization: {
                        chartType: 2
                        legendVisualization: {
                          isVisible: true
                          position: 2
                          hideSubtitle: false
                        }
                      }
                      timespan: {
                        relative: {
                          duration: 86400000
                        }
                      }
                    }
                  }
                }
              ]
            }
          }
          {
            position: {
              x: 8
              y: 3
              rowSpan: 3
              colSpan: 4
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MonitorChartPart'
              inputs: [
                {
                  name: 'options'
                  value: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: appInsightsId
                          }
                          name: 'customMetrics/ai.rag.embedding_duration'
                          aggregationType: 4
                          namespace: 'microsoft.insights/components'
                          metricVisualization: {
                            displayName: 'Embedding'
                            color: '#FFB900'
                          }
                        }
                        {
                          resourceMetadata: {
                            id: appInsightsId
                          }
                          name: 'customMetrics/ai.rag.search_duration'
                          aggregationType: 4
                          namespace: 'microsoft.insights/components'
                          metricVisualization: {
                            displayName: 'Search'
                            color: '#00B294'
                          }
                        }
                      ]
                      title: 'RAG Latency Breakdown (ms)'
                      titleKind: 1
                      visualization: {
                        chartType: 3 // Stacked Area
                        legendVisualization: {
                          isVisible: true
                          position: 2
                          hideSubtitle: false
                        }
                      }
                      timespan: {
                        relative: {
                          duration: 86400000
                        }
                      }
                    }
                  }
                }
              ]
            }
          }

          // =====================================================
          // Row 3: Tool Execution
          // =====================================================
          {
            position: {
              x: 0
              y: 6
              rowSpan: 1
              colSpan: 12
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MarkdownPart'
              inputs: []
              settings: {
                content: {
                  settings: {
                    content: '## Tool Execution\nEntity Extractor, Clause Analyzer, Document Classifier performance'
                  }
                }
              }
            }
          }
          {
            position: {
              x: 0
              y: 7
              rowSpan: 3
              colSpan: 4
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MonitorChartPart'
              inputs: [
                {
                  name: 'options'
                  value: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: appInsightsId
                          }
                          name: 'customMetrics/ai.tool.requests'
                          aggregationType: 1
                          namespace: 'microsoft.insights/components'
                          metricVisualization: {
                            displayName: 'Tool Executions'
                            color: '#8764B8'
                          }
                        }
                      ]
                      title: 'Tool Executions'
                      titleKind: 1
                      visualization: {
                        chartType: 2
                        legendVisualization: {
                          isVisible: true
                          position: 2
                          hideSubtitle: false
                        }
                      }
                      timespan: {
                        relative: {
                          duration: 86400000
                        }
                      }
                    }
                  }
                }
              ]
            }
          }
          {
            position: {
              x: 4
              y: 7
              rowSpan: 3
              colSpan: 4
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MonitorChartPart'
              inputs: [
                {
                  name: 'options'
                  value: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: appInsightsId
                          }
                          name: 'customMetrics/ai.tool.duration'
                          aggregationType: 4
                          namespace: 'microsoft.insights/components'
                          metricVisualization: {
                            displayName: 'Avg Duration'
                            color: '#E3008C'
                          }
                        }
                      ]
                      title: 'Tool Execution Latency (ms)'
                      titleKind: 1
                      visualization: {
                        chartType: 2
                        legendVisualization: {
                          isVisible: true
                          position: 2
                          hideSubtitle: false
                        }
                      }
                      timespan: {
                        relative: {
                          duration: 86400000
                        }
                      }
                    }
                  }
                }
              ]
            }
          }
          {
            position: {
              x: 8
              y: 7
              rowSpan: 3
              colSpan: 4
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MonitorChartPart'
              inputs: [
                {
                  name: 'options'
                  value: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: appInsightsId
                          }
                          name: 'customMetrics/ai.tool.tokens'
                          aggregationType: 1
                          namespace: 'microsoft.insights/components'
                          metricVisualization: {
                            displayName: 'Tool Tokens'
                            color: '#00B7C3'
                          }
                        }
                      ]
                      title: 'Tool Token Usage'
                      titleKind: 1
                      visualization: {
                        chartType: 2
                        legendVisualization: {
                          isVisible: true
                          position: 2
                          hideSubtitle: false
                        }
                      }
                      timespan: {
                        relative: {
                          duration: 86400000
                        }
                      }
                    }
                  }
                }
              ]
            }
          }

          // =====================================================
          // Row 4: Export Metrics
          // =====================================================
          {
            position: {
              x: 0
              y: 10
              rowSpan: 1
              colSpan: 12
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MarkdownPart'
              inputs: []
              settings: {
                content: {
                  settings: {
                    content: '## Export Operations\nDOCX, PDF, Email export success rates and performance'
                  }
                }
              }
            }
          }
          {
            position: {
              x: 0
              y: 11
              rowSpan: 3
              colSpan: 4
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MonitorChartPart'
              inputs: [
                {
                  name: 'options'
                  value: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: appInsightsId
                          }
                          name: 'customMetrics/ai.export.requests'
                          aggregationType: 1
                          namespace: 'microsoft.insights/components'
                          metricVisualization: {
                            displayName: 'Export Requests'
                            color: '#00B294'
                          }
                        }
                      ]
                      title: 'Export Requests'
                      titleKind: 1
                      visualization: {
                        chartType: 2
                        legendVisualization: {
                          isVisible: true
                          position: 2
                          hideSubtitle: false
                        }
                      }
                      timespan: {
                        relative: {
                          duration: 86400000
                        }
                      }
                    }
                  }
                }
              ]
            }
          }
          {
            position: {
              x: 4
              y: 11
              rowSpan: 3
              colSpan: 4
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MonitorChartPart'
              inputs: [
                {
                  name: 'options'
                  value: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: appInsightsId
                          }
                          name: 'customMetrics/ai.export.duration'
                          aggregationType: 4
                          namespace: 'microsoft.insights/components'
                          metricVisualization: {
                            displayName: 'Avg Duration'
                            color: '#FFB900'
                          }
                        }
                      ]
                      title: 'Export Latency (ms)'
                      titleKind: 1
                      visualization: {
                        chartType: 2
                        legendVisualization: {
                          isVisible: true
                          position: 2
                          hideSubtitle: false
                        }
                      }
                      timespan: {
                        relative: {
                          duration: 86400000
                        }
                      }
                    }
                  }
                }
              ]
            }
          }
          {
            position: {
              x: 8
              y: 11
              rowSpan: 3
              colSpan: 4
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MonitorChartPart'
              inputs: [
                {
                  name: 'options'
                  value: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: appInsightsId
                          }
                          name: 'customMetrics/ai.export.file_size'
                          aggregationType: 4
                          namespace: 'microsoft.insights/components'
                          metricVisualization: {
                            displayName: 'Avg File Size'
                            color: '#E3008C'
                          }
                        }
                      ]
                      title: 'Export File Size (bytes)'
                      titleKind: 1
                      visualization: {
                        chartType: 2
                        legendVisualization: {
                          isVisible: true
                          position: 2
                          hideSubtitle: false
                        }
                      }
                      timespan: {
                        relative: {
                          duration: 86400000
                        }
                      }
                    }
                  }
                }
              ]
            }
          }

          // =====================================================
          // Row 5: Circuit Breaker & Cache
          // =====================================================
          {
            position: {
              x: 0
              y: 14
              rowSpan: 1
              colSpan: 12
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MarkdownPart'
              inputs: []
              settings: {
                content: {
                  settings: {
                    content: '## Resilience & Cache\nCircuit breaker states and cache performance'
                  }
                }
              }
            }
          }
          {
            position: {
              x: 0
              y: 15
              rowSpan: 3
              colSpan: 4
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MonitorChartPart'
              inputs: [
                {
                  name: 'options'
                  value: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: appInsightsId
                          }
                          name: 'customMetrics/circuit_breaker.state_transitions'
                          aggregationType: 1
                          namespace: 'microsoft.insights/components'
                          metricVisualization: {
                            displayName: 'State Transitions'
                            color: '#D13438'
                          }
                        }
                      ]
                      title: 'Circuit Breaker State Transitions'
                      titleKind: 1
                      visualization: {
                        chartType: 2
                        legendVisualization: {
                          isVisible: true
                          position: 2
                          hideSubtitle: false
                        }
                      }
                      timespan: {
                        relative: {
                          duration: 86400000
                        }
                      }
                    }
                  }
                }
              ]
            }
          }
          {
            position: {
              x: 4
              y: 15
              rowSpan: 3
              colSpan: 4
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MonitorChartPart'
              inputs: [
                {
                  name: 'options'
                  value: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: appInsightsId
                          }
                          name: 'customMetrics/cache.hits'
                          aggregationType: 1
                          namespace: 'microsoft.insights/components'
                          metricVisualization: {
                            displayName: 'Cache Hits'
                            color: '#107C10'
                          }
                        }
                        {
                          resourceMetadata: {
                            id: appInsightsId
                          }
                          name: 'customMetrics/cache.misses'
                          aggregationType: 1
                          namespace: 'microsoft.insights/components'
                          metricVisualization: {
                            displayName: 'Cache Misses'
                            color: '#D13438'
                          }
                        }
                      ]
                      title: 'Cache Hit/Miss'
                      titleKind: 1
                      visualization: {
                        chartType: 2
                        legendVisualization: {
                          isVisible: true
                          position: 2
                          hideSubtitle: false
                        }
                      }
                      timespan: {
                        relative: {
                          duration: 86400000
                        }
                      }
                    }
                  }
                }
              ]
            }
          }
          {
            position: {
              x: 8
              y: 15
              rowSpan: 3
              colSpan: 4
            }
            metadata: {
              type: 'Extension/HubsExtension/PartType/MonitorChartPart'
              inputs: [
                {
                  name: 'options'
                  value: {
                    chart: {
                      metrics: [
                        {
                          resourceMetadata: {
                            id: appInsightsId
                          }
                          name: 'customMetrics/cache.latency'
                          aggregationType: 4
                          namespace: 'microsoft.insights/components'
                          metricVisualization: {
                            displayName: 'Avg Latency'
                            color: '#0078D4'
                          }
                        }
                      ]
                      title: 'Cache Latency (ms)'
                      titleKind: 1
                      visualization: {
                        chartType: 2
                        legendVisualization: {
                          isVisible: true
                          position: 2
                          hideSubtitle: false
                        }
                      }
                      timespan: {
                        relative: {
                          duration: 86400000
                        }
                      }
                    }
                  }
                }
              ]
            }
          }
        ]
      }
    ]
    metadata: {
      model: {
        timeRange: {
          value: {
            relative: {
              duration: 24
              timeUnit: 1 // Hours
            }
          }
          type: 'MsPortalFx.Composition.Configuration.ValueTypes.TimeRange'
        }
        filterLocale: {
          value: 'en-us'
        }
        filters: {
          value: {
            MsPortalFx_TimeRange: {
              model: {
                format: 'utc'
                granularity: 'auto'
                relative: '24h'
              }
            }
          }
        }
      }
    }
  }
}

output dashboardId string = dashboard.id
output dashboardName string = dashboard.name
