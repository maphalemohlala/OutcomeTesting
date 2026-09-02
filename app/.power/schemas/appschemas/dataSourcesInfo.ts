/*!
 * Copyright (C) Microsoft Corporation. All rights reserved.
 * This file is auto-generated. Do not modify it manually.
 * Changes to this file may be overwritten.
 */

export const dataSourcesInfo = {
  "al_assigncase": {
    "tableId": "",
    "version": "",
    "primaryKey": "",
    "dataSourceType": "Dataverse",
    "apis": {
      "al_AssignCase": {
        "path": "/api/data/v9.2/al_AssignCase",
        "method": "POST",
        "parameters": [
          {
            "name": "TargetId",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "AssigneeEmail",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "ReviewInstanceId",
            "in": "body",
            "required": false,
            "type": "string"
          },
          {
            "name": "Team",
            "in": "body",
            "required": false,
            "type": "string"
          },
          {
            "name": "Reason",
            "in": "body",
            "required": false,
            "type": "string"
          },
          {
            "name": "ExpectedRowVersion",
            "in": "body",
            "required": false,
            "type": "string"
          },
          {
            "name": "IdempotencyKey",
            "in": "body",
            "required": true,
            "type": "string"
          }
        ],
        "responseInfo": {
          "200": {
            "type": "object"
          }
        }
      }
    }
  },
  "al_assignuserrole": {
    "tableId": "",
    "version": "",
    "primaryKey": "",
    "dataSourceType": "Dataverse",
    "apis": {
      "al_AssignUserRole": {
        "path": "/api/data/v9.2/al_AssignUserRole",
        "method": "POST",
        "parameters": [
          {
            "name": "UserEmail",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "AppRole",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "IdempotencyKey",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "RoleCode",
            "in": "body",
            "required": false,
            "type": "string"
          }
        ],
        "responseInfo": {
          "200": {
            "type": "object"
          }
        }
      }
    }
  },
  "al_auditevents": {
    "tableId": "",
    "version": "",
    "primaryKey": "al_auditeventid",
    "dataSourceType": "Dataverse",
    "apis": {}
  },
  "al_completeremediation": {
    "tableId": "",
    "version": "",
    "primaryKey": "",
    "dataSourceType": "Dataverse",
    "apis": {
      "al_CompleteRemediation": {
        "path": "/api/data/v9.2/al_CompleteRemediation",
        "method": "POST",
        "parameters": [
          {
            "name": "TargetId",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "ExpectedRowVersion",
            "in": "body",
            "required": false,
            "type": "string"
          },
          {
            "name": "IdempotencyKey",
            "in": "body",
            "required": true,
            "type": "string"
          }
        ],
        "responseInfo": {
          "200": {
            "type": "object"
          }
        }
      }
    }
  },
  "al_createexportbatch": {
    "tableId": "",
    "version": "",
    "primaryKey": "",
    "dataSourceType": "Dataverse",
    "apis": {
      "al_CreateExportBatch": {
        "path": "/api/data/v9.2/al_CreateExportBatch",
        "method": "POST",
        "parameters": [
          {
            "name": "Name",
            "in": "body",
            "required": false,
            "type": "string"
          },
          {
            "name": "IdempotencyKey",
            "in": "body",
            "required": true,
            "type": "string"
          }
        ],
        "responseInfo": {
          "200": {
            "type": "object"
          }
        }
      }
    }
  },
  "al_createrole": {
    "tableId": "",
    "version": "",
    "primaryKey": "",
    "dataSourceType": "Dataverse",
    "apis": {
      "al_CreateRole": {
        "path": "/api/data/v9.2/al_CreateRole",
        "method": "POST",
        "parameters": [
          {
            "name": "RoleName",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "Description",
            "in": "body",
            "required": false,
            "type": "string"
          },
          {
            "name": "IdempotencyKey",
            "in": "body",
            "required": true,
            "type": "string"
          }
        ],
        "responseInfo": {
          "200": {
            "type": "object"
          }
        }
      }
    }
  },
  "al_createuser": {
    "tableId": "",
    "version": "",
    "primaryKey": "",
    "dataSourceType": "Dataverse",
    "apis": {
      "al_CreateUser": {
        "path": "/api/data/v9.2/al_CreateUser",
        "method": "POST",
        "parameters": [
          {
            "name": "FullName",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "WorkEmail",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "IdempotencyKey",
            "in": "body",
            "required": true,
            "type": "string"
          }
        ],
        "responseInfo": {
          "200": {
            "type": "object"
          }
        }
      }
    }
  },
  "al_exportbatches": {
    "tableId": "",
    "version": "",
    "primaryKey": "al_exportbatchid",
    "dataSourceType": "Dataverse",
    "apis": {}
  },
  "al_exportrecords": {
    "tableId": "",
    "version": "",
    "primaryKey": "al_exportrecordid",
    "dataSourceType": "Dataverse",
    "apis": {}
  },
  "al_generateexport": {
    "tableId": "",
    "version": "",
    "primaryKey": "",
    "dataSourceType": "Dataverse",
    "apis": {
      "al_GenerateExport": {
        "path": "/api/data/v9.2/al_GenerateExport",
        "method": "POST",
        "parameters": [
          {
            "name": "BatchId",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "IdempotencyKey",
            "in": "body",
            "required": true,
            "type": "string"
          }
        ],
        "responseInfo": {
          "200": {
            "type": "object"
          }
        }
      }
    }
  },
  "al_importbatches": {
    "tableId": "",
    "version": "",
    "primaryKey": "al_importbatchid",
    "dataSourceType": "Dataverse",
    "apis": {}
  },
  "al_importexceptions": {
    "tableId": "",
    "version": "",
    "primaryKey": "al_importexceptionid",
    "dataSourceType": "Dataverse",
    "apis": {}
  },
  "al_outcomecases": {
    "tableId": "",
    "version": "",
    "primaryKey": "al_outcomecaseid",
    "dataSourceType": "Dataverse",
    "apis": {}
  },
  "al_outcomes": {
    "tableId": "",
    "version": "",
    "primaryKey": "al_outcomeid",
    "dataSourceType": "Dataverse",
    "apis": {}
  },
  "al_pagepermissions": {
    "tableId": "",
    "version": "",
    "primaryKey": "al_pagepermissionid",
    "dataSourceType": "Dataverse",
    "apis": {}
  },
  "al_questions": {
    "tableId": "",
    "version": "",
    "primaryKey": "al_questionid",
    "dataSourceType": "Dataverse",
    "apis": {}
  },
  "al_questionversions": {
    "tableId": "",
    "version": "",
    "primaryKey": "al_questionversionid",
    "dataSourceType": "Dataverse",
    "apis": {}
  },
  "al_remediationactions": {
    "tableId": "",
    "version": "",
    "primaryKey": "al_remediationactionid",
    "dataSourceType": "Dataverse",
    "apis": {}
  },
  "al_responses": {
    "tableId": "",
    "version": "",
    "primaryKey": "al_responseid",
    "dataSourceType": "Dataverse",
    "apis": {}
  },
  "al_retireandsucceedquestion": {
    "tableId": "",
    "version": "",
    "primaryKey": "",
    "dataSourceType": "Dataverse",
    "apis": {
      "al_RetireAndSucceedQuestion": {
        "path": "/api/data/v9.2/al_RetireAndSucceedQuestion",
        "method": "POST",
        "parameters": [
          {
            "name": "QuestionId",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "NewWording",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "IdempotencyKey",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "ResponseType",
            "in": "body",
            "required": false,
            "type": "string"
          },
          {
            "name": "Mandatory",
            "in": "body",
            "required": false,
            "type": "string"
          }
        ],
        "responseInfo": {
          "200": {
            "type": "object"
          }
        }
      }
    }
  },
  "al_reviewinstances": {
    "tableId": "",
    "version": "",
    "primaryKey": "al_reviewinstanceid",
    "dataSourceType": "Dataverse",
    "apis": {}
  },
  "al_roles": {
    "tableId": "",
    "version": "",
    "primaryKey": "al_roleid",
    "dataSourceType": "Dataverse",
    "apis": {}
  },
  "al_sections": {
    "tableId": "",
    "version": "",
    "primaryKey": "al_sectionid",
    "dataSourceType": "Dataverse",
    "apis": {}
  },
  "al_setpagepermission": {
    "tableId": "",
    "version": "",
    "primaryKey": "",
    "dataSourceType": "Dataverse",
    "apis": {
      "al_SetPagePermission": {
        "path": "/api/data/v9.2/al_SetPagePermission",
        "method": "POST",
        "parameters": [
          {
            "name": "AppRole",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "ResourceKey",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "AccessLevel",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "IdempotencyKey",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "RoleCode",
            "in": "body",
            "required": false,
            "type": "string"
          }
        ],
        "responseInfo": {
          "200": {
            "type": "object"
          }
        }
      }
    }
  },
  "al_setpermissionruleactive": {
    "tableId": "",
    "version": "",
    "primaryKey": "",
    "dataSourceType": "Dataverse",
    "apis": {
      "al_SetPermissionRuleActive": {
        "path": "/api/data/v9.2/al_SetPermissionRuleActive",
        "method": "POST",
        "parameters": [
          {
            "name": "PermissionId",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "Active",
            "in": "body",
            "required": true,
            "type": "boolean"
          },
          {
            "name": "IdempotencyKey",
            "in": "body",
            "required": true,
            "type": "string"
          }
        ],
        "responseInfo": {
          "200": {
            "type": "object"
          }
        }
      }
    }
  },
  "al_setroleassignmentactive": {
    "tableId": "",
    "version": "",
    "primaryKey": "",
    "dataSourceType": "Dataverse",
    "apis": {
      "al_SetRoleAssignmentActive": {
        "path": "/api/data/v9.2/al_SetRoleAssignmentActive",
        "method": "POST",
        "parameters": [
          {
            "name": "MappingId",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "Active",
            "in": "body",
            "required": true,
            "type": "boolean"
          },
          {
            "name": "IdempotencyKey",
            "in": "body",
            "required": true,
            "type": "string"
          }
        ],
        "responseInfo": {
          "200": {
            "type": "object"
          }
        }
      }
    }
  },
  "al_setuseractive": {
    "tableId": "",
    "version": "",
    "primaryKey": "",
    "dataSourceType": "Dataverse",
    "apis": {
      "al_SetUserActive": {
        "path": "/api/data/v9.2/al_SetUserActive",
        "method": "POST",
        "parameters": [
          {
            "name": "UserId",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "Active",
            "in": "body",
            "required": true,
            "type": "boolean"
          },
          {
            "name": "IdempotencyKey",
            "in": "body",
            "required": true,
            "type": "string"
          }
        ],
        "responseInfo": {
          "200": {
            "type": "object"
          }
        }
      }
    }
  },
  "al_signoffs": {
    "tableId": "",
    "version": "",
    "primaryKey": "al_signoffid",
    "dataSourceType": "Dataverse",
    "apis": {}
  },
  "al_updatecasedetails": {
    "tableId": "",
    "version": "",
    "primaryKey": "",
    "dataSourceType": "Dataverse",
    "apis": {
      "al_UpdateCaseDetails": {
        "path": "/api/data/v9.2/al_UpdateCaseDetails",
        "method": "POST",
        "parameters": [
          {
            "name": "TargetId",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "Status",
            "in": "body",
            "required": false,
            "type": "string"
          },
          {
            "name": "RouteId",
            "in": "body",
            "required": false,
            "type": "string"
          },
          {
            "name": "Priority",
            "in": "body",
            "required": false,
            "type": "string"
          },
          {
            "name": "DueDate",
            "in": "body",
            "required": false,
            "type": "string"
          },
          {
            "name": "Reason",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "ExpectedRowVersion",
            "in": "body",
            "required": false,
            "type": "string"
          },
          {
            "name": "IdempotencyKey",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "Fields",
            "in": "body",
            "required": false,
            "type": "string"
          }
        ],
        "responseInfo": {
          "200": {
            "type": "object"
          }
        }
      }
    }
  },
  "al_updaterole": {
    "tableId": "",
    "version": "",
    "primaryKey": "",
    "dataSourceType": "Dataverse",
    "apis": {
      "al_UpdateRole": {
        "path": "/api/data/v9.2/al_UpdateRole",
        "method": "POST",
        "parameters": [
          {
            "name": "RoleId",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "RoleName",
            "in": "body",
            "required": false,
            "type": "string"
          },
          {
            "name": "Description",
            "in": "body",
            "required": false,
            "type": "string"
          },
          {
            "name": "Active",
            "in": "body",
            "required": false,
            "type": "boolean"
          },
          {
            "name": "ExpectedRowVersion",
            "in": "body",
            "required": false,
            "type": "string"
          },
          {
            "name": "IdempotencyKey",
            "in": "body",
            "required": true,
            "type": "string"
          }
        ],
        "responseInfo": {
          "200": {
            "type": "object"
          }
        }
      }
    }
  },
  "al_updateuser": {
    "tableId": "",
    "version": "",
    "primaryKey": "",
    "dataSourceType": "Dataverse",
    "apis": {
      "al_UpdateUser": {
        "path": "/api/data/v9.2/al_UpdateUser",
        "method": "POST",
        "parameters": [
          {
            "name": "UserId",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "FullName",
            "in": "body",
            "required": true,
            "type": "string"
          },
          {
            "name": "ExpectedRowVersion",
            "in": "body",
            "required": false,
            "type": "string"
          },
          {
            "name": "IdempotencyKey",
            "in": "body",
            "required": true,
            "type": "string"
          }
        ],
        "responseInfo": {
          "200": {
            "type": "object"
          }
        }
      }
    }
  },
  "al_userrolemappings": {
    "tableId": "",
    "version": "",
    "primaryKey": "al_userrolemappingid",
    "dataSourceType": "Dataverse",
    "apis": {}
  },
  "al_users": {
    "tableId": "",
    "version": "",
    "primaryKey": "al_userid",
    "dataSourceType": "Dataverse",
    "apis": {}
  }
};
