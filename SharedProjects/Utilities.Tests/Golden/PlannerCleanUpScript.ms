	local bLayers = for n in bLayerNames collect (if n == "" then undefined else (LayerManager.getLayerFromName n))
	for i = 1 to bLayers.count where bLayerNames[i] != "" and bLayers[i] == undefined do (format "FAIL\tlayerfind\t%\tgone\n" i to:bLog)
	local bNodes = #()
	for i = 1 to bHandles.count do
	(
		bNode = undefined
		if bHandles[i] != 0 do
		(
			bNode = maxOps.getNodeByHandle bHandles[i]
			if (bNode == undefined) then
			(
				format "FAIL\tfind\t%\tgone\n" i to:bLog
			)
			else if (bNode.name != bNames[i]) then
			(
				format "FAIL\tfind\t%\tchanged\n" i to:bLog
				bNode = undefined
			)
		)
		append bNodes bNode
	)
	local bStepped = for i = 1 to bLayerRenameIdx.count collect false
	try
	(
		undo "Clean up scene graph" on
		(
			with redraw off
			(

				for i = 1 to bRenameNode.count do
				(
					try
					(
						bNode = bNodes[bRenameNode[i]]
						if (bNode != undefined) do
						(
							bNode.name = bRenameNew[i]
							if (bNode.name == bRenameNew[i]) then (if (bRenameKind[i] == 2) then (bObjectsRenamed += 1) else (bHelpersRenamed += 1)) else (format "FAIL\trename\t%\trefused\n" i to:bLog)
						)
					)
					catch
					(
						bDetail = (getCurrentException()) as string
						bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
						format "FAIL\trename\t%\terror\t%\n" i bDetail to:bLog
					)
				)

				for i = 1 to bLayerRenameIdx.count do
				(
					try
					(
						bLayer = bLayers[bLayerRenameIdx[i]]
						if (bLayer == undefined) then
						(
							format "FAIL\tlayer\t%\tgone\n" i to:bLog
						)
						else
						(
							bLayer.setName bLayerRenameTemp[i]
							if (bLayer.name == bLayerRenameTemp[i]) then (bStepped[i] = true) else (format "FAIL\tlayer\t%\trefused\n" i to:bLog)
						)
					)
					catch
					(
						bDetail = (getCurrentException()) as string
						bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
						format "FAIL\tlayer\t%\terror\t%\n" i bDetail to:bLog
					)
				)

				for i = 1 to bLayerRenameIdx.count where bStepped[i] do
				(
					try
					(
						bLayer = bLayers[bLayerRenameIdx[i]]
						bLayer.setName bLayerRenameNew[i]
						if (bLayer.name == bLayerRenameNew[i]) then
						(
							bLayersRenamed += 1
						)
						else
						(
							bLayer.setName bLayerNames[bLayerRenameIdx[i]]
							if (bLayer.name == bLayerNames[bLayerRenameIdx[i]]) then (format "FAIL\tlayer\t%\ttaken\n" i to:bLog) else (format "FAIL\tlayer\t%\tstuck\n" i to:bLog)
						)
					)
					catch
					(
						bDetail = (getCurrentException()) as string
						bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
						format "FAIL\tlayer\t%\terror\t%\n" i bDetail to:bLog
					)
				)

				for i = 1 to bNewLayer.count do
				(
					try
					(
						bLayer = LayerManager.newLayerFromName bNewLayerName[i]
						if (bLayer == undefined) then
						(
							format "FAIL\tlayercreate\t%\ttaken\n" i to:bLog
						)
						else
						(
							bLayers[bNewLayer[i]] = bLayer
							bLayersCreated += 1
							if (bNewLayerParent[i] > 0) do
							(
								bParent = bLayers[bNewLayerParent[i]]
								if (bParent == undefined) then (format "FAIL\tlayerparent\t%\tgone\n" i to:bLog) else (bLayer.setParent bParent)
							)
						)
					)
					catch
					(
						bDetail = (getCurrentException()) as string
						bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
						format "FAIL\tlayercreate\t%\terror\t%\n" i bDetail to:bLog
					)
				)

				for i = 1 to bLayerMoveChild.count do
				(
					try
					(
						bLayer = bLayers[bLayerMoveChild[i]]
						bParent = bLayers[bLayerMoveParent[i]]
						if (bLayer == undefined or bParent == undefined) then
						(
							format "FAIL\tlayermove\t%\tgone\n" i to:bLog
						)
						else
						(
							bLayer.setParent bParent
							bOther = bLayer.getParent()
							if (bOther != undefined and bOther.name == bParent.name) then (bLayersMoved += 1) else (format "FAIL\tlayermove\t%\trefused\n" i to:bLog)
						)
					)
					catch
					(
						bDetail = (getCurrentException()) as string
						bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
						format "FAIL\tlayermove\t%\terror\t%\n" i bDetail to:bLog
					)
				)

				for i = 1 to bNewNode.count do
				(
					try
					(
						bLayer = if (bNewNodeLayer[i] > 0) then bLayers[bNewNodeLayer[i]] else undefined
						if (bLayer == undefined) then
						(
							format "FAIL\thelpercreate\t%\tnolayer\n" i to:bLog
						)
						else
						(
							bNode = Dummy name:bNewNodeName[i] pos:[0,0,0] boxsize:[1,1,1]
							bLayer.addNode bNode
							bNodes[bNewNode[i]] = bNode
							bHelpersCreated += 1
							if (bNewNodeId[i] != "") do
							(
								setUserProp bNode "planner_id" bNewNodeId[i]
								bStamped += 1
							)
							if (bNewNodeParent[i] > 0) do
							(
								bParent = bNodes[bNewNodeParent[i]]
								if (bParent == undefined) then
								(
									format "FAIL\thelperparent\t%\tgone\n" i to:bLog
								)
								else
								(
									if (isGroupHead bParent) then
									(
										if (isGroupMember bNode) do (detachNodesFromGroup #(bNode))
										attachNodesToGroup #(bNode) bParent
									)
									else
									(
										if ((isGroupMember bNode) and not (isGroupMember bParent)) do (detachNodesFromGroup #(bNode))
										bNode.parent = bParent
									)
									if (bNode.parent != bParent) do (format "FAIL\thelperparent\t%\trefused\n" i to:bLog)
								)
							)
						)
					)
					catch
					(
						bDetail = (getCurrentException()) as string
						bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
						format "FAIL\thelpercreate\t%\terror\t%\n" i bDetail to:bLog
					)
				)

				for i = 1 to bMoveNode.count do
				(
					try
					(
						bNode = bNodes[bMoveNode[i]]
						bParent = bNodes[bMoveParent[i]]
						if (bNode == undefined or bParent == undefined) then
						(
							format "FAIL\tmove\t%\tgone\n" i to:bLog
						)
						else
						(
							if (isGroupHead bParent) then
							(
								if (isGroupMember bNode) do (detachNodesFromGroup #(bNode))
								attachNodesToGroup #(bNode) bParent
							)
							else
							(
								if ((isGroupMember bNode) and not (isGroupMember bParent)) do (detachNodesFromGroup #(bNode))
								bNode.parent = bParent
							)
							if (bNode.parent == bParent) then (bMoved += 1) else (format "FAIL\tmove\t%\trefused\n" i to:bLog)
						)
					)
					catch
					(
						bDetail = (getCurrentException()) as string
						bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
						format "FAIL\tmove\t%\terror\t%\n" i bDetail to:bLog
					)
				)

				for i = 1 to bLinkNode.count do
				(
					try
					(
						bNode = bNodes[bLinkNode[i]]
						bParent = bNodes[bLinkParent[i]]
						if (bNode == undefined or bParent == undefined) then
						(
							format "FAIL\tlink\t%\tgone\n" i to:bLog
						)
						else
						(
							if (isGroupHead bParent) then
							(
								if (isGroupMember bNode) do (detachNodesFromGroup #(bNode))
								attachNodesToGroup #(bNode) bParent
							)
							else
							(
								if ((isGroupMember bNode) and not (isGroupMember bParent)) do (detachNodesFromGroup #(bNode))
								bNode.parent = bParent
							)
							if (bNode.parent == bParent) then (bLinked += 1) else (format "FAIL\tlink\t%\trefused\n" i to:bLog)
						)
					)
					catch
					(
						bDetail = (getCurrentException()) as string
						bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
						format "FAIL\tlink\t%\terror\t%\n" i bDetail to:bLog
					)
				)

				for i = 1 to bPutNode.count do
				(
					try
					(
						bNode = bNodes[bPutNode[i]]
						bLayer = bLayers[bPutLayer[i]]
						if (bNode == undefined or bLayer == undefined) then
						(
							format "FAIL\tput\t%\tgone\n" i to:bLog
						)
						else
						(
							bLayer.addNode bNode
							if (bNode.layer != undefined and bNode.layer.name == bLayer.name) then (if (bPutKind[i] == 1) then (bHelpersPut += 1) else (bPut += 1)) else (format "FAIL\tput\t%\trefused\n" i to:bLog)
						)
					)
					catch
					(
						bDetail = (getCurrentException()) as string
						bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
						format "FAIL\tput\t%\terror\t%\n" i bDetail to:bLog
					)
				)

				for i = 1 to bStampNode.count do
				(
					try
					(
						bNode = bNodes[bStampNode[i]]
						if (bNode != undefined) do
						(
							setUserProp bNode "planner_id" bStampId[i]
							bStamped += 1
						)
					)
					catch
					(
						bDetail = (getCurrentException()) as string
						bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
						format "FAIL\tstamp\t%\terror\t%\n" i bDetail to:bLog
					)
				)
			)
		)
	)
	catch
	(
		bDetail = (getCurrentException()) as string
		bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
		format "ABORT\t%\n" bDetail to:bLog
	)
	format "DONE\t%\t%\t%\t%\t%\t%\t%\t%\t%\t%\t%\n" bHelpersRenamed bObjectsRenamed bMoved bLinked bPut bLayersMoved bLayersRenamed bLayersCreated bHelpersCreated bHelpersPut bStamped to:bLog
	format "PLANNERCLEANUP-END\n" to:bLog
	bLog as string
)
