/**
 * EntityConfigurationService Unit Tests
 */

import { EntityConfigurationService } from '../EntityConfigurationService';

describe('EntityConfigurationService', () => {
  beforeEach(() => {
    // Reset service state before each test
    EntityConfigurationService.loadConfiguration(null);
  });

  describe('loadConfiguration', () => {
    it('should load valid JSON configuration', () => {
      const validConfig = JSON.stringify({
        schemaVersion: "1.0",
        defaultConfig: {
          viewMode: "Grid",
          enabledCommands: ["open", "create"]
        },
        entityConfigs: {
          account: {
            viewMode: "Card"
          }
        }
      });

      EntityConfigurationService.loadConfiguration(validConfig);
      expect(EntityConfigurationService.isConfigurationLoaded()).toBe(true);
    });

    it('should handle invalid JSON gracefully', () => {
      const invalidJson = "{ invalid json }";
      const consoleErrorSpy = jest.spyOn(console, 'error').mockImplementation();

      EntityConfigurationService.loadConfiguration(invalidJson);

      expect(consoleErrorSpy).toHaveBeenCalled();
      expect(EntityConfigurationService.isConfigurationLoaded()).toBe(true); // Falls back to default

      consoleErrorSpy.mockRestore();
    });

    it('should handle null configuration', () => {
      EntityConfigurationService.loadConfiguration(null);
      expect(EntityConfigurationService.isConfigurationLoaded()).toBe(true);

      const config = EntityConfigurationService.getEntityConfiguration("account");
      expect(config.viewMode).toBe("Grid"); // Default value
    });

    it('should handle undefined configuration', () => {
      EntityConfigurationService.loadConfiguration(undefined);
      expect(EntityConfigurationService.isConfigurationLoaded()).toBe(true);
    });

    it('should validate schema version and fallback on mismatch', () => {
      const invalidVersionConfig = JSON.stringify({
        schemaVersion: "2.0", // Unsupported version
        defaultConfig: {},
        entityConfigs: {}
      });

      const consoleWarnSpy = jest.spyOn(console, 'warn').mockImplementation();

      EntityConfigurationService.loadConfiguration(invalidVersionConfig);

      expect(consoleWarnSpy).toHaveBeenCalledWith(
        expect.stringContaining("Unsupported schema version: 2.0")
      );

      consoleWarnSpy.mockRestore();
    });
  });

  describe('getEntityConfiguration', () => {
    it('should merge entity config with defaults', () => {
      const config = JSON.stringify({
        schemaVersion: "1.0",
        defaultConfig: {
          viewMode: "Grid",
          enabledCommands: ["open", "create"],
          compactToolbar: false,
          enableVirtualization: true,
          rowHeight: 44
        },
        entityConfigs: {
          account: {
            viewMode: "Card",
            compactToolbar: true
          }
        }
      });

      EntityConfigurationService.loadConfiguration(config);
      const accountConfig = EntityConfigurationService.getEntityConfiguration("account");

      expect(accountConfig.viewMode).toBe("Card"); // Override
      expect(accountConfig.compactToolbar).toBe(true); // Override
      expect(accountConfig.enabledCommands).toEqual(["open", "create"]); // From default
      expect(accountConfig.enableVirtualization).toBe(true); // From default
      expect(accountConfig.rowHeight).toBe(44); // From default
    });

    it('should return defaults for unknown entity', () => {
      const config = JSON.stringify({
        schemaVersion: "1.0",
        defaultConfig: {
          viewMode: "List",
          enabledCommands: ["open"]
        },
        entityConfigs: {}
      });

      EntityConfigurationService.loadConfiguration(config);
      const unknownConfig = EntityConfigurationService.getEntityConfiguration("unknown_entity");

      expect(unknownConfig.viewMode).toBe("List");
      expect(unknownConfig.enabledCommands).toEqual(["open"]);
    });

    it('should override defaults with entity-specific config', () => {
      const config = JSON.stringify({
        schemaVersion: "1.0",
        defaultConfig: {
          viewMode: "Grid",
          enabledCommands: ["open", "create", "delete"]
        },
        entityConfigs: {
          contact: {
            viewMode: "Card",
            enabledCommands: ["open", "sendEmail"]
          }
        }
      });

      EntityConfigurationService.loadConfiguration(config);
      const contactConfig = EntityConfigurationService.getEntityConfiguration("contact");

      expect(contactConfig.viewMode).toBe("Card");
      expect(contactConfig.enabledCommands).toEqual(["open", "sendEmail"]);
    });

    it('should merge custom commands from default and entity config', () => {
      const config = JSON.stringify({
        schemaVersion: "1.0",
        defaultConfig: {
          customCommands: {
            refresh: {
              label: "Refresh All",
              actionType: "function",
              actionName: "RefreshAll"
            }
          }
        },
        entityConfigs: {
          sprk_document: {
            customCommands: {
              upload: {
                label: "Upload",
                actionType: "customapi",
                actionName: "sprk_Upload"
              }
            }
          }
        }
      });

      EntityConfigurationService.loadConfiguration(config);
      const docConfig = EntityConfigurationService.getEntityConfiguration("sprk_document");

      expect(docConfig.customCommands).toHaveProperty("refresh");
      expect(docConfig.customCommands).toHaveProperty("upload");
      expect(docConfig.customCommands.upload.label).toBe("Upload");
    });

    it('should handle case-insensitive entity names', () => {
      const config = JSON.stringify({
        schemaVersion: "1.0",
        defaultConfig: {
          viewMode: "Grid"
        },
        entityConfigs: {
          account: {
            viewMode: "List"
          }
        }
      });

      EntityConfigurationService.loadConfiguration(config);

      const lowerConfig = EntityConfigurationService.getEntityConfiguration("account");
      const upperConfig = EntityConfigurationService.getEntityConfiguration("ACCOUNT");
      const mixedConfig = EntityConfigurationService.getEntityConfiguration("Account");

      expect(lowerConfig.viewMode).toBe("List");
      expect(upperConfig.viewMode).toBe("List");
      expect(mixedConfig.viewMode).toBe("List");
    });
  });

  describe('getCustomCommand', () => {
    beforeEach(() => {
      const config = JSON.stringify({
        schemaVersion: "1.0",
        defaultConfig: {},
        entityConfigs: {
          sprk_document: {
            customCommands: {
              upload: {
                label: "Upload Document",
                actionType: "customapi",
                actionName: "sprk_UploadDocument"
              }
            }
          }
        }
      });

      EntityConfigurationService.loadConfiguration(config);
    });

    it('should get custom command by key', () => {
      const uploadCommand = EntityConfigurationService.getCustomCommand("sprk_document", "upload");

      expect(uploadCommand).toBeDefined();
      expect(uploadCommand?.label).toBe("Upload Document");
      expect(uploadCommand?.actionType).toBe("customapi");
    });

    it('should return undefined for unknown command', () => {
      const unknownCommand = EntityConfigurationService.getCustomCommand("sprk_document", "unknown");
      expect(unknownCommand).toBeUndefined();
    });

    it('should return undefined for unknown entity', () => {
      const command = EntityConfigurationService.getCustomCommand("unknown_entity", "upload");
      expect(command).toBeUndefined();
    });
  });

  describe('validateConfiguration', () => {
    it('should validate correct configuration', () => {
      const validConfig = JSON.stringify({
        schemaVersion: "1.0",
        defaultConfig: {
          viewMode: "Grid"
        },
        entityConfigs: {
          account: {
            customCommands: {
              test: {
                label: "Test",
                actionType: "customapi",
                actionName: "TestAction"
              }
            }
          }
        }
      });

      const result = EntityConfigurationService.validateConfiguration(validConfig);

      expect(result.valid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });

    it('should detect missing schemaVersion', () => {
      const config = JSON.stringify({
        defaultConfig: {},
        entityConfigs: {}
      });

      const result = EntityConfigurationService.validateConfiguration(config);

      expect(result.valid).toBe(false);
      expect(result.errors).toContain("Missing schemaVersion");
    });

    it('should detect missing defaultConfig', () => {
      const config = JSON.stringify({
        schemaVersion: "1.0",
        entityConfigs: {}
      });

      const result = EntityConfigurationService.validateConfiguration(config);

      expect(result.valid).toBe(false);
      expect(result.errors).toContain("Missing defaultConfig");
    });

    it('should detect missing entityConfigs', () => {
      const config = JSON.stringify({
        schemaVersion: "1.0",
        defaultConfig: {}
      });

      const result = EntityConfigurationService.validateConfiguration(config);

      expect(result.valid).toBe(false);
      expect(result.errors).toContain("Missing entityConfigs");
    });

    it('should validate custom command required fields', () => {
      const config = JSON.stringify({
        schemaVersion: "1.0",
        defaultConfig: {},
        entityConfigs: {
          account: {
            customCommands: {
              invalid: {
                // Missing label, actionType, actionName
              }
            }
          }
        }
      });

      const result = EntityConfigurationService.validateConfiguration(config);

      expect(result.valid).toBe(false);
      expect(result.errors).toContain("account.invalid: Missing label");
      expect(result.errors).toContain("account.invalid: Missing actionType");
      expect(result.errors).toContain("account.invalid: Missing actionName");
    });

    it('should handle invalid JSON', () => {
      const invalidJson = "{ not valid json";

      const result = EntityConfigurationService.validateConfiguration(invalidJson);

      expect(result.valid).toBe(false);
      expect(result.errors[0]).toContain("Invalid JSON");
    });
  });

  describe('isConfigurationLoaded', () => {
    it('should return true after loading configuration', () => {
      const config = JSON.stringify({
        schemaVersion: "1.0",
        defaultConfig: {},
        entityConfigs: {}
      });

      EntityConfigurationService.loadConfiguration(config);
      expect(EntityConfigurationService.isConfigurationLoaded()).toBe(true);
    });

    it('should return true after loading null (uses default)', () => {
      EntityConfigurationService.loadConfiguration(null);
      expect(EntityConfigurationService.isConfigurationLoaded()).toBe(true);
    });
  });
});
